using Ligature.Platform.Persistence.Audit;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The catalogue moved out of provisioning and into the deployment phase,
/// which changed one thing about it that matters more than the move: it now
/// runs on EVERY deployment rather than once per tenant.
///
/// It used to insert into an empty catalogue and fail loudly on a duplicate.
/// That was correct while AUD-C4 owned it. It would now make the second
/// `./up.sh` fail, so these tests exist to hold the new contract: reconcile,
/// never duplicate, never delete.
/// </summary>
public sealed class AuditCatalogueDeployerTests
{
    /// <summary>
    /// The reason `up.sh` can be run twice. A deployment that treated its own
    /// previous run as an error would make the ordinary case an outage.
    /// </summary>
    [Fact]
    public async Task Deploying_twice_leaves_the_catalogue_identical()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        var first = await SnapshotAsync(database);

        Assert.NotEmpty(first);

        await new AuditCatalogueDeployer(database.ConnectionString).DeployAsync();

        var second = await SnapshotAsync(database);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Deployment is a precondition of the host, so the fixture that installs
    /// a database must leave a catalogue behind. If this fails, `up.sh` would
    /// produce a host that refuses to start on IMPL-08.
    /// </summary>
    [Fact]
    public async Task Installing_a_database_leaves_an_active_catalogue()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        Assert.NotEqual(0L, await ScalarAsync(
            database, "SELECT count(*) FROM audit.audit_event_type WHERE is_active"));

        Assert.NotEqual(0L, await ScalarAsync(
            database, "SELECT count(*) FROM audit.audit_event_origin WHERE is_active"));
    }

    /// <summary>
    /// An entry the release no longer declares is RETIRED, not removed.
    ///
    /// A committed audit_record references its event type, so a catalogue
    /// that could delete rows would be a catalogue that could orphan the
    /// trail. Deactivation is the only retirement the trail can survive.
    /// </summary>
    [Fact]
    public async Task An_entry_the_release_no_longer_declares_is_deactivated_not_deleted()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        // A row from an imagined earlier release, copied from a real one so
        // it satisfies every ET check constraint and differs only by code.
        // Nothing in the compiled seed declares it, so this deployment should
        // retire it.
        await ExecuteAsync(
            database,
            """
            INSERT INTO audit.audit_event_type
            SELECT 'WithdrawnByAnEarlierRelease', version, owning_context,
                   'Withdrawn', description, default_classification,
                   reason_required, write_path, shape, primary_entity_type,
                   primary_entity_required, entity_ref_roles, pii_paths,
                   payload_schema_ref, true
              FROM audit.audit_event_type
             ORDER BY code
             LIMIT 1
            """);

        await new AuditCatalogueDeployer(database.ConnectionString).DeployAsync();

        Assert.Equal(1L, await ScalarAsync(
            database,
            """
            SELECT count(*) FROM audit.audit_event_type
            WHERE code = 'WithdrawnByAnEarlierRelease'
            """));

        Assert.Equal(0L, await ScalarAsync(
            database,
            """
            SELECT count(*) FROM audit.audit_event_type
            WHERE code = 'WithdrawnByAnEarlierRelease' AND is_active
            """));
    }

    /// <summary>
    /// A definition that changed between releases is updated in place. Without
    /// this the catalogue would keep the old release's definition forever, and
    /// IMPL-08 would compare the handlers against a stale row.
    /// </summary>
    [Fact]
    public async Task A_changed_definition_is_updated_rather_than_duplicated()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        var code = await ScalarStringAsync(
            database,
            "SELECT code FROM audit.audit_event_type ORDER BY code LIMIT 1");

        await ExecuteAsync(
            database,
            $"""
            UPDATE audit.audit_event_type
               SET name = 'DriftedName', is_active = false
             WHERE code = '{code}'
            """);

        await new AuditCatalogueDeployer(database.ConnectionString).DeployAsync();

        Assert.Equal(0L, await ScalarAsync(
            database,
            $"""
            SELECT count(*) FROM audit.audit_event_type
            WHERE code = '{code}' AND name = 'DriftedName'
            """));

        // One row, not two: the release definition replaced the drifted one.
        Assert.Equal(1L, await ScalarAsync(
            database,
            $"SELECT count(*) FROM audit.audit_event_type WHERE code = '{code}'"));
    }

    // ------------------------------------------------------------ helpers

    /// <summary>
    /// Every column that reconciliation may write, ordered, as one comparable
    /// string per row. A snapshot that only counted rows would not notice a
    /// second run rewriting a definition.
    /// </summary>
    private static async Task<List<string>> SnapshotAsync(ThrowawayDatabase database)
    {
        var rows = new List<string>();

        await using var connection = new NpgsqlConnection(database.ConnectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT code, version, owning_context, name, default_classification,
                   reason_required, write_path, shape, primary_entity_type,
                   primary_entity_required, entity_ref_roles::text,
                   pii_paths::text, is_active
            FROM audit.audit_event_type
            ORDER BY code, version
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var values = new object?[reader.FieldCount];

            reader.GetValues(values!);

            rows.Add(string.Join('|', values));
        }

        return rows;
    }

    private static async Task ExecuteAsync(ThrowawayDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(ThrowawayDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarStringAsync(
        ThrowawayDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        return (string)(await command.ExecuteScalarAsync())!;
    }
}
