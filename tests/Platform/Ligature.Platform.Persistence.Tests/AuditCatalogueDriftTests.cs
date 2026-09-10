using System.Text.Json;
using Ligature.Platform.Persistence.Audit;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The event catalogue is reconciled by Ligature.AuditSchema on every
/// deployment, so editing AuditEventCatalogue in code and re-deploying now
/// updates a database rather than silently leaving it behind. This still reads
/// the deployed rows and compares them to what the code would seed today,
/// field for field, because "the deployment reconciles it" is a claim worth
/// checking rather than assuming — a database that missed a deployment, or a
/// reconcile that skipped a column, both show up here.
///
/// Note the asymmetry: the PERMISSION catalogue is NOT reconciled. PRV-C2 is
/// still unimplemented, so adding a permission changes nothing for an existing
/// database, and CatalogueDriftTests covers that hazard for real.
///
/// A drift here is more than a stale row. The catalogue is what AR4 and AR5
/// resolve against and what behaviour 13 will copy onto every record, so a
/// database whose catalogue disagrees with the release is a database whose
/// records will state rules the release does not.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable, when the audit schema is not
/// deployed, or when the database was provisioned before AUD-C4 existed —
/// each with a message naming the remedy.
/// </summary>
public sealed class AuditCatalogueDriftTests
{
    [Fact]
    public async Task Event_catalogue_matches_the_database()
    {
        await using var connection = await TestDatabase.OpenAsync();

        await RequireDeployedAndProvisionedAsync(connection);

        var expected = AuditEventCatalogue.GetEventTypeSeeds()
            .ToDictionary(x => x.Code, StringComparer.Ordinal);

        var actual = new Dictionary<string, DeployedEventType>(StringComparer.Ordinal);

        await using (var command = new NpgsqlCommand(
            """
            SELECT code, version, owning_context, name, default_classification,
                   reason_required, write_path, shape, primary_entity_type,
                   primary_entity_required, entity_ref_roles::text, pii_paths::text,
                   payload_schema_ref, is_active
            FROM audit.audit_event_type
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actual[reader.GetString(0)] = new DeployedEventType(
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetBoolean(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.GetBoolean(9),
                    JsonSerializer.Deserialize<List<EntityRefRoleSeed>>(reader.GetString(10))!,
                    JsonSerializer.Deserialize<List<PiiPathSeed>>(reader.GetString(11))!,
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.GetBoolean(13));
            }
        }

        Assert.Equal(expected.Count, actual.Count);

        foreach (var (code, seed) in expected)
        {
            Assert.True(
                actual.ContainsKey(code),
                $"Event type '{code}' exists in code but not in the database. "
                + "The database was provisioned before it was added.");

            var row = actual[code];

            Assert.Equal(seed.Version, row.Version);
            Assert.Equal(seed.OwningContext, row.OwningContext);
            Assert.Equal(seed.Name, row.Name);
            Assert.Equal(seed.DefaultClassification, row.DefaultClassification);
            Assert.Equal(seed.ReasonRequired, row.ReasonRequired);
            Assert.Equal(seed.WritePath, row.WritePath);
            Assert.Equal(seed.Shape, row.Shape);
            Assert.Equal(seed.PrimaryEntityType, row.PrimaryEntityType);
            Assert.Equal(seed.PrimaryEntityRequired, row.PrimaryEntityRequired);
            Assert.Equal(seed.EntityRefRoles, row.EntityRefRoles);
            Assert.Equal(seed.PiiPaths, row.PiiPaths);
            Assert.Null(row.PayloadSchemaRef);
            Assert.Equal(seed.IsActive, row.IsActive);
        }
    }

    [Fact]
    public async Task Event_origins_match_the_database()
    {
        await using var connection = await TestDatabase.OpenAsync();

        await RequireDeployedAndProvisionedAsync(connection);

        var expected = AuditEventCatalogue.GetEventTypeSeeds()
            .SelectMany(x => x.Origins.Select(o => (x.Code, o, x.IsActive)))
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.o, StringComparer.Ordinal)
            .ToList();

        var actual = new List<(string Code, string o, bool IsActive)>();

        await using (var command = new NpgsqlCommand(
            "SELECT code, origin_kind, is_active FROM audit.audit_event_origin", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                actual.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
        }

        // Sorted here, with the same comparer as the expected side, rather
        // than by the server. The database's locale collation folds case
        // and orders 'SignedOut' before 'SignInFailed'; ordinal does the
        // reverse. The test is about content, not about whose sort wins.
        var ordered = actual
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.o, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, ordered);
    }

    /// <summary>
    /// Three ways for the target to be unassessable, each named. The last is
    /// the one a developer will actually hit: a database provisioned before
    /// AUD-C4 existed never receives the catalogue, because PRV-C1's sentinel
    /// takes its "already present" branch. The remedy is a reset, by
    /// decision (AGENTS.md section 3), not a retroactive seed.
    /// </summary>
    private static async Task RequireDeployedAndProvisionedAsync(NpgsqlConnection connection)
    {
        await using var deployed = new NpgsqlCommand(
            "SELECT to_regclass('audit.audit_event_type') IS NOT NULL", connection);

        Assert.True(
            (bool)(await deployed.ExecuteScalarAsync())!,
            "The target database has no audit schema. Deploy it with "
            + "Ligature.AuditSchema after the migrations; drift cannot be assessed.");

        await using var provisioned = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_event_type", connection);

        var rows = (long)(await provisioned.ExecuteScalarAsync())!;

        Assert.True(
            rows > 0,
            "The target database has an audit schema but an empty catalogue. It "
            + "was provisioned before AUD-C4 existed, and PRV-C1 will not seed it "
            + "retroactively: recreate the database and provision it again.");
    }

    private sealed record DeployedEventType(
        int Version,
        string OwningContext,
        string Name,
        string DefaultClassification,
        bool ReasonRequired,
        string WritePath,
        string Shape,
        string? PrimaryEntityType,
        bool PrimaryEntityRequired,
        List<EntityRefRoleSeed> EntityRefRoles,
        List<PiiPathSeed> PiiPaths,
        string? PayloadSchemaRef,
        bool IsActive);
}
