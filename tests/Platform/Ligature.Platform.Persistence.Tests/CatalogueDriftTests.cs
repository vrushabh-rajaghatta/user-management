using Ligature.Platform.Persistence.Provisioning;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// PRV-C1 seeds the catalogues once and then no-ops forever, so a provisioned
/// database keeps the values it was seeded with. These tests read the deployed
/// rows and compare them against what the code would seed today.
///
/// PRV-C2 now provides the cure, and these remain the POSTCONDITION of it: the
/// deployed catalogue matches the release-controlled one. A failure here still
/// means what it always meant — this database has not been reconciled with the
/// release — but the remedy is no longer hand-written SQL:
///
///     dotnet run --project src/Tools/Ligature.CatalogueSync
///
/// Detection and convergence are deliberately separate. These tests prove the
/// state; CatalogueSynchronisationTests proves that synchronisation reaches it,
/// and that drift it may not resolve is refused instead.
///
/// They read RELEASE-OWNED rows only. PRV-C2 Amendment 1 (PRV1, PRV5) put
/// tenant roles and their grants outside catalogue reconciliation, and AUT-C3
/// lets a tenant create one; a database holding tenant roles is not drifted.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class CatalogueDriftTests
{

    [Fact]
    public async Task Permission_catalogue_matches_the_database()
    {
        await using var connection = await TestDatabase.OpenAsync();

        var expected = PlatformProvisioner.GetPermissionSeeds()
            .ToDictionary(
                x => x.Code,
                x => (x.Name, x.Resource, x.Action, x.RequiresHumanActor, x.Description),
                StringComparer.Ordinal);

        var actual = new Dictionary<
            string, (string, string, string, bool, string?)>(StringComparer.Ordinal);

        await using (var command = new NpgsqlCommand(
            "SELECT code, name, resource, action, requires_human_actor, description "
            + "FROM permission", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actual[reader.GetString(0)] = (
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5));
            }
        }

        SkipIfUnprovisioned(actual.Count, "permission");

        Assert.Equal(expected.Count, actual.Count);

        foreach (var (code, want) in expected)
        {
            Assert.True(
                actual.ContainsKey(code),
                $"Permission '{code}' exists in code but not in the database. "
                + "The database was provisioned before it was added.");

            Assert.Equal(want, actual[code]);
        }
    }

    [Fact]
    public async Task Role_catalogue_matches_the_database()
    {
        await using var connection = await TestDatabase.OpenAsync();

        var expected = PlatformProvisioner.GetRoleSeeds()
            .ToDictionary(
                x => x.Code,
                x => (x.Name, (string?)x.Description),
                StringComparer.Ordinal);

        var actual = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);

        await using (var command = new NpgsqlCommand(
            // RELEASE-OWNED ROWS ONLY (PRV-C2 Amendment 1, PRV1). A tenant role
            // created by AUT-C3 is outside catalogue reconciliation, so it is
            // outside this postcondition too; counting it here would fail the
            // suite on every database where someone created one.
            "SELECT code, name, description FROM role WHERE is_system_role", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actual[reader.GetString(0)] = (
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2));
            }
        }

        SkipIfUnprovisioned(actual.Count, "role");

        Assert.Equal(expected.Count, actual.Count);

        foreach (var (code, want) in expected)
        {
            Assert.True(
                actual.ContainsKey(code),
                $"Role '{code}' exists in code but not in the database.");

            Assert.Equal(want, actual[code]);
        }
    }

    [Fact]
    public async Task Role_permission_grants_match_the_database()
    {
        await using var connection = await TestDatabase.OpenAsync();

        var expected = PlatformProvisioner.GetRolePermissionSeeds()
            .Select(x => $"{x.RoleCode} -> {x.PermissionCode}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var actual = new List<string>();

        await using (var command = new NpgsqlCommand(
            """
            SELECT r.code, p.code
            FROM role_permission rp
            JOIN role r ON r.id = rp.role_id
            JOIN permission p ON p.id = rp.permission_id
            -- A grant's ownership follows its role (PRV5).
            WHERE rp.revoked_at IS NULL AND r.is_system_role
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                actual.Add($"{reader.GetString(0)} -> {reader.GetString(1)}");
        }

        SkipIfUnprovisioned(actual.Count, "role_permission");

        actual.Sort(StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    private static void SkipIfUnprovisioned(int rowCount, string table)
    {
        Assert.True(
            rowCount > 0,
            $"The target database has no rows in '{table}'. It has not been "
            + "provisioned by PRV-C1, so drift cannot be assessed.");
    }

}
