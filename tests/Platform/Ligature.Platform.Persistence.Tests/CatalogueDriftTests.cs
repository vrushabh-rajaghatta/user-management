using Ligature.Platform.Persistence.Provisioning;
using System.Net.Sockets;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// PRV-C1 seeds the catalogues once and then no-ops forever, so a provisioned
/// database silently keeps the values it was seeded with. Editing a catalogue
/// in code therefore causes drift that nothing else reports. These tests read
/// the deployed rows and compare them against what the code would seed today.
///
/// Target database comes from LIGATURE_CONNECTION; the tests skip when no
/// database is reachable, so they do not break a build without PostgreSQL.
/// </summary>
public sealed class CatalogueDriftTests
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres";

    [Fact]
    public async Task Permission_catalogue_matches_the_database()
    {
        await using var connection = await OpenAsync();

        if (connection is null)
            return;

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
        await using var connection = await OpenAsync();

        if (connection is null)
            return;

        var expected = PlatformProvisioner.GetRoleSeeds()
            .ToDictionary(
                x => x.Code,
                x => (x.Name, (string?)x.Description),
                StringComparer.Ordinal);

        var actual = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);

        await using (var command = new NpgsqlCommand(
            "SELECT code, name, description FROM role", connection))
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
        await using var connection = await OpenAsync();

        if (connection is null)
            return;

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
            WHERE rp.revoked_at IS NULL
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

    private static async Task<NpgsqlConnection?> OpenAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("LIGATURE_CONNECTION")
            ?? DefaultConnection;

        var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync();

            return connection;
        }
        catch (NpgsqlException)
        {
            await connection.DisposeAsync();

            return null;
        }
        catch (SocketException)
        {
            await connection.DisposeAsync();

            return null;
        }
    }
}
