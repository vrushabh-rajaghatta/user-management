using Ligature.Platform.Domain.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Audit;
using Ligature.Platform.Persistence.Provisioning;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// AUD-C4 — ProvisionAudit, as PRV-C1 runs it. Every test starts from a
/// throwaway database built the way an installation is built (migrations,
/// then the audit schema), because the shared database is already provisioned
/// and its sentinel would take the "already present" branch.
///
/// Two kinds of test. The first proves the seed: a provisioned tenant holds
/// the release catalogue, its origins, and retention v1, and its trail is in
/// the state its first record expects. The second proves the REFUSAL: a
/// tenant whose boundary is wrong, or whose trail has already been touched,
/// is not handed over — and nothing at all is committed, because
/// provisioning is one transaction.
///
/// The refusals are what "configuration assertions are not evidence" means
/// here: provisioning does not trust that Ligature.AuditSchema ran; it
/// establishes the handover facts itself.
/// </summary>
public sealed class AuditProvisioningTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    // ----------------------------------------------------------- the seed

    [Fact]
    public async Task PRV_C1_seeds_the_release_event_catalogue()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionAsync(database);

        var seeds = AuditEventCatalogue.GetEventTypeSeeds();

        Assert.Equal(seeds.Count, await CountAsync(database, "audit.audit_event_type"));
        Assert.Equal(
            seeds.Sum(x => x.Origins.Count),
            await CountAsync(database, "audit.audit_event_origin"));

        // Every seed is version 1 and this release's version is recorded as
        // such — the value TenantProvisioned will carry in its payload.
        Assert.Equal(1, AuditEventCatalogue.Version);
        Assert.Equal(0, await CountAsync(database, "audit.audit_event_type WHERE version <> 1"));
    }

    /// <summary>
    /// AU11 reserves the Agent actor type; its four events are seeded so their
    /// codes exist and are retired by IsActive rather than absent (ET9), and
    /// their origins follow so AUD-C3 switches both on together.
    /// </summary>
    [Fact]
    public async Task The_deferred_agent_events_are_seeded_inactive_with_their_origins()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionAsync(database);

        var inactive = await ScalarAsync<string>(database,
            "SELECT string_agg(code, ',' ORDER BY code) FROM audit.audit_event_type WHERE NOT is_active");

        Assert.Equal(
            "AgentCredentialIssued,AgentOwnershipTransferred,AgentRegistered,AgentVersionActivated",
            inactive);

        Assert.Equal(
            0,
            await CountAsync(database,
                "audit.audit_event_origin o JOIN audit.audit_event_type t USING (code, version) "
                + "WHERE t.is_active <> o.is_active"));
    }

    /// <summary>
    /// EO5 — the whole of AUD-5 at the data level. AR5's foreign key makes
    /// this the ONLY way a record with no actor can exist, so which types
    /// declare Anonymous is a security fact, not a seed detail.
    /// </summary>
    [Fact]
    public async Task Anonymous_origin_is_declared_for_exactly_the_two_types_EO5_permits()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionAsync(database);

        var anonymous = await ScalarAsync<string>(database,
            "SELECT string_agg(code, ',' ORDER BY code) FROM audit.audit_event_origin "
            + "WHERE origin_kind = 'Anonymous'");

        Assert.Equal("SignInFailed,TokenRejected", anonymous);
    }

    [Fact]
    public async Task Retention_policy_version_1_is_seeded_from_the_release_baseline()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionAsync(database);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT policy_version, minimum_retention_months, reason, created_by, effective_from "
            + "FROM audit.audit_retention_policy",
            connection);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "retention v1 must exist (RT7)");
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(AuditReleaseBaseline.MinimumRetentionMonths, reader.GetInt32(1));
        Assert.Equal("Provisioning baseline", reader.GetString(2));
        Assert.Equal(User.SystemUserId.Value, reader.GetGuid(3));
        Assert.Equal(Now, reader.GetFieldValue<DateTimeOffset>(4));
        Assert.False(await reader.ReadAsync(), "exactly one version is seeded (RT7)");
    }

    /// <summary>
    /// The JSON columns carry the workbook's own key casing, so a validation
    /// reviewer comparing the deployed catalogue to the Entity Workbook reads
    /// the same names in both.
    /// </summary>
    [Fact]
    public async Task Structured_columns_use_the_workbook_key_names()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionAsync(database);

        var refs = await ScalarAsync<string>(database,
            "SELECT entity_ref_roles::text FROM audit.audit_event_type WHERE code = 'RoleGranted'");

        Assert.Contains("\"EntityType\": \"User\"", refs);
        Assert.Contains("\"RefRole\": \"Subject\"", refs);
        Assert.Contains("\"Required\": true", refs);

        var pii = await ScalarAsync<string>(database,
            "SELECT pii_paths::text FROM audit.audit_event_type WHERE code = 'IdentityCreated'");

        Assert.Contains("\"path\": \"After.Username\"", pii);
        Assert.Contains("\"describes\": \"RefRole:Subject\"", pii);
    }

    [Fact]
    public async Task A_provisioned_tenant_has_an_empty_unconsumed_trail()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionAsync(database);

        Assert.Equal(0, await CountAsync(database, "audit.audit_record"));

        // Stricter than "no rows": a rolled-back write consumes a value
        // (AUD-D32), and the first record must be Sequence 1 (AUD-S10).
        Assert.False(await ScalarAsync<bool>(database,
            "SELECT is_called FROM audit.audit_record_sequence_seq"));
    }

    [Fact]
    public async Task PRV_C1_remains_idempotent_with_the_audit_seed()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionAsync(database);
        await ProvisionAsync(database);

        Assert.Equal(
            AuditEventCatalogue.GetEventTypeSeeds().Count,
            await CountAsync(database, "audit.audit_event_type"));

        Assert.Equal(1, await CountAsync(database, "audit.audit_retention_policy"));
    }

    // ------------------------------------------------------- the refusals

    /// <summary>
    /// AUD-C4 step 2: "any deviation aborts provisioning. A tenant is never
    /// handed over with a writable trail." Simulated as the deployment
    /// defect it would be — a grant that should not exist — and the whole
    /// provisioning, System actor included, must roll back.
    /// </summary>
    [Fact]
    public async Task A_tenant_whose_application_role_can_update_the_trail_is_not_handed_over()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ExecuteAsync(database, "GRANT UPDATE ON audit.audit_record TO app_role");

        var failure = await Assert.ThrowsAsync<ProvisioningException>(
            () => ProvisionAsync(database));

        Assert.Contains("UPDATE on audit.audit_record", failure.Message);

        await AssertNothingProvisionedAsync(database);
    }

    /// <summary>
    /// AUD-S10: the first record must be Sequence 1. A consumed sequence on an
    /// empty trail means something already attempted a write, and the probe
    /// that later expects Sequence 1 would fail for a reason nobody could see.
    /// Refused at provisioning instead, where the cause is nameable.
    /// </summary>
    [Fact]
    public async Task A_tenant_whose_audit_sequence_was_consumed_is_not_handed_over()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ExecuteAsync(database, "SELECT nextval('audit.audit_record_sequence_seq')");

        var failure = await Assert.ThrowsAsync<ProvisioningException>(
            () => ProvisionAsync(database));

        Assert.Contains("Sequence 1", failure.Message);

        await AssertNothingProvisionedAsync(database);
    }

    /// <summary>
    /// Provisioning does not trust that Ligature.AuditSchema ran because it
    /// runs earlier in the chain. A migrated database with no audit schema is
    /// refused with the remedy, before anything is seeded.
    /// </summary>
    [Fact]
    public async Task A_tenant_without_the_audit_schema_is_not_handed_over()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(auditDeployed: false);

        var failure = await Assert.ThrowsAsync<ProvisioningException>(
            () => ProvisionAsync(database));

        Assert.Contains("not deployed", failure.Message);
        Assert.Contains("Ligature.AuditSchema", failure.Message);

        Assert.Equal(0, await CountAsync(database, "app_user"));
    }

    // ------------------------------------------------------------ helpers

    private static async Task ProvisionAsync(ThrowawayDatabase database)
    {
        await using var context = database.CreateContext();

        await new PlatformProvisioner(context).ProvisionAsync(Now, CancellationToken.None);
    }

    private static async Task AssertNothingProvisionedAsync(ThrowawayDatabase database)
    {
        Assert.Equal(0, await CountAsync(database, "app_user"));
        Assert.Equal(0, await CountAsync(database, "permission"));
        Assert.Equal(0, await CountAsync(database, "audit.audit_event_type"));
        Assert.Equal(0, await CountAsync(database, "audit.audit_retention_policy"));
    }

    private static async Task<long> CountAsync(ThrowawayDatabase database, string from)
        => await ScalarAsync<long>(database, $"SELECT count(*) FROM {from}");

    private static async Task<T> ScalarAsync<T>(ThrowawayDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(ThrowawayDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
