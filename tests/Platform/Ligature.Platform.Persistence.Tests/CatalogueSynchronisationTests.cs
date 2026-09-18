using System.Text.Json;
using Ligature.Platform.Persistence.Audit;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Provisioning;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// PRV-C2, against a real database (docs/requirements.md, PRV-C2).
///
/// Every test builds a throwaway database the way an installation is built, and
/// then introduces ONE divergence, because the contract's whole shape is that
/// direction decides the outcome:
///
///     seed -> database   additive reconciliation
///     database -> seed   drift, and a refusal
///
/// The refusal tests all assert the same second thing — that nothing was
/// committed — because "it refused" and "it refused without mutating" are
/// different claims and only the second is the invariant.
/// </summary>
public sealed class CatalogueSynchronisationTests
{
    private const string Release = "test-release";

    // ------------------------------------------------------------ bootstrap

    /// <summary>
    /// A11. The provisioner sits behind the bootstrap Compose profile, so
    /// ./up.sh legitimately reaches a database with schema and no System actor.
    /// There is no catalogue to reconcile and no actor to attribute an event
    /// to, so this is a successful no-op that records NOTHING — not a
    /// synchronisation with an empty result, and not drift.
    /// </summary>
    [Fact]
    public async Task An_unprovisioned_database_is_a_successful_no_op_that_records_nothing()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        // Deployed, so nothing here depends on the catalogue being absent: the
        // no-op is about there being no System actor, not about audit.
        await new AuditCatalogueDeployer(database.PrivilegedConnection).DeployAsync();

        var result = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.NotProvisioned, result.Outcome);
        Assert.Equal(CatalogueSyncCounts.None, result.Counts);
        Assert.Empty(result.Refusals);
        Assert.Equal(0, await AuditRecordCountAsync(database));
    }

    // ---------------------------------------------------------- convergence

    /// <summary>A1, A3 — the defect this requirement exists to fix.</summary>
    [Fact]
    public async Task A_permission_the_release_adds_reaches_an_existing_database()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database, """
            DELETE FROM role_permission
             WHERE permission_id IN (SELECT id FROM permission WHERE code = 'user.read');
            DELETE FROM permission WHERE code = 'user.read';
            """);

        var result = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Counts.PermissionsInserted);
        Assert.Equal(1, await CountAsync(database, "SELECT count(*) FROM permission WHERE code = 'user.read'"));

        await AssertConvergedAsync(database);
    }

    /// <summary>A2.</summary>
    [Fact]
    public async Task A_role_the_release_adds_reaches_an_existing_database()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database, """
            DELETE FROM role_permission
             WHERE role_id IN (SELECT id FROM role WHERE code = 'access-reviewer');
            DELETE FROM role WHERE code = 'access-reviewer';
            """);

        var result = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Counts.RolesInserted);
        Assert.True(result.Counts.GrantsInserted > 0, "the role's grants were not restored with it");

        await AssertConvergedAsync(database);
    }

    /// <summary>A4 — the only mutation to an existing row this is entitled to make.</summary>
    [Fact]
    public async Task Metadata_that_drifted_is_reconciled_to_the_catalogue()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database,
            "UPDATE permission SET name = 'Stale name' WHERE code = 'user.read'");

        var result = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Counts.PermissionMetadataReconciled);
        Assert.Equal(
            "View Users",
            await ScalarAsync(database, "SELECT name FROM permission WHERE code = 'user.read'"));

        await AssertConvergedAsync(database);
    }

    /// <summary>A10 — a run that changes nothing is a success, not a skip.</summary>
    [Fact]
    public async Task A_database_already_current_is_synchronised_without_mutation()
    {
        await using var database = await ProvisionedAsync();

        var result = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.Counts.Total);
    }

    /// <summary>
    /// A3 — the grant case on its own. A release that grants an existing
    /// permission to an existing role changes no catalogue entry at all, so
    /// nothing but the grant table would notice it was missing.
    /// </summary>
    [Fact]
    public async Task A_grant_the_release_adds_reaches_an_existing_database()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database, """
            DELETE FROM role_permission
             WHERE role_id = (SELECT id FROM role WHERE code = 'access-reviewer')
               AND permission_id = (SELECT id FROM permission WHERE code = 'user.read');
            """);

        var result = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Counts.GrantsInserted);
        Assert.Equal(0, result.Counts.PermissionsInserted);

        await AssertConvergedAsync(database);
    }

    /// <summary>
    /// PRV-C1 Amendment 1, S3 — how a tenant provisioned before the amendment
    /// receives it. The database is the one that release would have seeded:
    /// everything today's seed has except security-administrator's user.read.
    /// Exactly that grant arrives, attributed to the System actor, and a second
    /// deployment finds nothing to do.
    /// </summary>
    [Fact]
    public async Task A_tenant_provisioned_before_the_amendment_gains_security_administrator_user_read()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database, """
            DELETE FROM role_permission
             WHERE role_id = (SELECT id FROM role WHERE code = 'security-administrator')
               AND permission_id = (SELECT id FROM permission WHERE code = 'user.read');
            """);

        var first = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.Succeeded, first.Outcome);
        Assert.Equal(1, first.Counts.GrantsInserted);
        Assert.Equal(1, first.Counts.Total);

        Assert.Equal(1, await CountAsync(database, $"""
            SELECT count(*)
              FROM role_permission rp
              JOIN role r       ON r.id = rp.role_id
              JOIN permission p ON p.id = rp.permission_id
             WHERE r.code = 'security-administrator'
               AND p.code = 'user.read'
               AND rp.revoked_at IS NULL
               AND rp.granted_by = '{Ligature.Platform.Domain.Users.User.SystemUserId.Value}'
            """));

        await AssertConvergedAsync(database);

        var second = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.Succeeded, second.Outcome);
        Assert.Equal(0, second.Counts.Total);
    }

    /// <summary>
    /// F5, and PRV-C1 Amendment 1's S4: why that amendment is one-way. A grant
    /// the database holds and the release does not list is what a release
    /// dropping security-administrator's user.read would present, and it is
    /// refused, naming the grant, with nothing committed. Revoking it would
    /// contract authorization for every holder of the role at deploy time.
    /// </summary>
    [Fact]
    public async Task A_grant_the_catalogue_does_not_list_is_refused_not_revoked()
    {
        await using var database = await ProvisionedAsync();

        // A grant no seed has ever listed, inserted the way only a privileged
        // hand could; the release under test lists everything else.
        await ExecuteAsync(database, $"""
            INSERT INTO role_permission (id, role_id, permission_id, granted_at, granted_by)
            SELECT gen_random_uuid(), r.id, p.id, now(), '{Ligature.Platform.Domain.Users.User.SystemUserId.Value}'
              FROM role r, permission p
             WHERE r.code = 'access-reviewer'
               AND p.code = 'user.update';
            """);

        var result = await SynchroniseAsync(database);

        AssertRefused(result, CatalogueRefusalReason.GrantMissingFromSeed, "access-reviewer/user.update");

        Assert.Equal(1, await CountAsync(database, """
            SELECT count(*)
              FROM role_permission rp
              JOIN role r       ON r.id = rp.role_id
              JOIN permission p ON p.id = rp.permission_id
             WHERE r.code = 'access-reviewer'
               AND p.code = 'user.update'
               AND rp.revoked_at IS NULL
            """));
    }

    /// <summary>
    /// A18, and the one that matters most for a real release: several additive
    /// changes at once, converged in ONE run. A release rarely adds exactly one
    /// thing, and a synchroniser that handled each kind only in isolation would
    /// pass every test above and still not do its job.
    /// </summary>
    [Fact]
    public async Task Several_additive_changes_converge_in_one_run()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database, """
            DELETE FROM role_permission
             WHERE permission_id IN (SELECT id FROM permission WHERE code = 'user.read');
            DELETE FROM permission WHERE code = 'user.read';

            DELETE FROM role_permission
             WHERE role_id IN (SELECT id FROM role WHERE code = 'access-reviewer');
            DELETE FROM role WHERE code = 'access-reviewer';

            DELETE FROM role_permission
             WHERE role_id = (SELECT id FROM role WHERE code = 'user-administrator')
               AND permission_id = (SELECT id FROM permission WHERE code = 'user.unlock');

            UPDATE permission SET name = 'Stale name' WHERE code = 'user.create';
            """);

        var result = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Counts.PermissionsInserted);
        Assert.Equal(1, result.Counts.RolesInserted);
        Assert.Equal(1, result.Counts.PermissionMetadataReconciled);
        Assert.True(result.Counts.GrantsInserted >= 3, $"only {result.Counts.GrantsInserted} grants restored");

        await AssertConvergedAsync(database);
    }

    /// <summary>
    /// CLASSIFICATION PRECEDES MUTATION, ACROSS HETEROGENEOUS FINDINGS — and
    /// this test is why M5 was withdrawn.
    ///
    /// Four additive things to do and one reason not to, in one run. Every
    /// single-change test passed while role metadata drift threw a domain
    /// exception out of the middle of a synchronisation, because none of them
    /// put the two kinds of finding together.
    ///
    /// Role metadata is refused rather than reconciled: Role.UpdateMetadata
    /// declines a system role, and every seeded role is one.
    /// </summary>
    [Fact]
    public async Task Additive_work_alongside_role_metadata_drift_refuses_everything()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database, """
            DELETE FROM role_permission
             WHERE permission_id IN (SELECT id FROM permission WHERE code = 'user.read');
            DELETE FROM permission WHERE code = 'user.read';

            DELETE FROM role_permission
             WHERE role_id IN (SELECT id FROM role WHERE code = 'access-reviewer');
            DELETE FROM role WHERE code = 'access-reviewer';

            UPDATE role SET name = 'Stale role name' WHERE code = 'user-administrator';
            """);

        var result = await SynchroniseAsync(database);

        AssertRefused(result, CatalogueRefusalReason.SecuritySemanticDrift, "user-administrator");

        // Nothing additive was applied "while we are here".
        Assert.Equal(0, await CountAsync(database, "SELECT count(*) FROM permission WHERE code = 'user.read'"));
        Assert.Equal(0, await CountAsync(database, "SELECT count(*) FROM role WHERE code = 'access-reviewer'"));

        // The refusal is evidenced despite the rollback.
        Assert.Equal(1, await AuditRecordCountAsync(database));
    }

    // ------------------------------------------------------------- refusals

    /// <summary>
    /// A5. THE DIRECTION TEST. A permission the catalogue no longer lists is
    /// far more often a bad merge or a rename than a deliberate retirement, and
    /// deactivating it would remove authorization from every holder at deploy
    /// time.
    /// </summary>
    [Fact]
    public async Task A_permission_the_catalogue_does_not_list_is_refused()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database, """
            INSERT INTO permission
                (id, code, name, description, resource, action, requires_human_actor,
                 is_active, created_at, created_by)
            SELECT gen_random_uuid(), 'ghost.permission', 'Ghost', NULL, 'Ghost', 'Read',
                   false, true, now(), created_by
              FROM permission LIMIT 1;
            """);

        var result = await SynchroniseAsync(database);

        AssertRefused(result, CatalogueRefusalReason.PermissionMissingFromSeed, "ghost.permission");
        Assert.Equal(1, await CountAsync(database, "SELECT count(*) FROM permission WHERE code = 'ghost.permission'"));
    }

    /// <summary>A6 — security semantics are never reconciled.</summary>
    [Fact]
    public async Task A_requires_human_actor_difference_is_refused_not_repaired()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database,
            "UPDATE permission SET requires_human_actor = NOT requires_human_actor WHERE code = 'user.create'");

        var result = await SynchroniseAsync(database);

        AssertRefused(result, CatalogueRefusalReason.SecuritySemanticDrift, "user.create");

        Assert.False(
            await BoolAsync(database, "SELECT requires_human_actor FROM permission WHERE code = 'user.create'"),
            "the drifted value was repaired; it must be refused and left alone");
    }

    /// <summary>A7 — a deploy must not undo an explicit deactivation.</summary>
    [Fact]
    public async Task An_inactive_permission_the_catalogue_lists_is_refused_not_reactivated()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database,
            "UPDATE permission SET is_active = false WHERE code = 'user.read'");

        var result = await SynchroniseAsync(database);

        AssertRefused(result, CatalogueRefusalReason.InactiveCatalogueEntry, "user.read");

        Assert.False(
            await BoolAsync(database, "SELECT is_active FROM permission WHERE code = 'user.read'"),
            "the permission was reactivated; a deploy may not reverse an operator's decision");
    }

    /// <summary>A8, A15 — revoked means a human decided, and a grant row is immutable.</summary>
    [Fact]
    public async Task A_revoked_grant_the_catalogue_lists_is_refused_not_regranted()
    {
        await using var database = await ProvisionedAsync();

        // revoked_at and revoked_by are a pair — ck_role_permission_revocation_pair
        // requires both or neither, which is the database refusing a half-told
        // revocation.
        await ExecuteAsync(database, """
            UPDATE role_permission
               SET revoked_at = now(), revoked_by = granted_by
             WHERE role_id = (SELECT id FROM role WHERE code = 'access-reviewer')
               AND permission_id = (SELECT id FROM permission WHERE code = 'user.read');
            """);

        var result = await SynchroniseAsync(database);

        AssertRefused(result, CatalogueRefusalReason.RevokedGrantInSeed, "access-reviewer/user.read");

        Assert.Equal(
            1,
            await CountAsync(database, """
                SELECT count(*) FROM role_permission
                 WHERE role_id = (SELECT id FROM role WHERE code = 'access-reviewer')
                   AND permission_id = (SELECT id FROM permission WHERE code = 'user.read')
                   AND revoked_at IS NOT NULL
                """));
    }

    /// <summary>A9 — every refusal, one assertion: nothing was committed.</summary>
    [Fact]
    public async Task A_refusal_commits_no_catalogue_mutation_even_when_other_work_was_available()
    {
        await using var database = await ProvisionedAsync();

        // One thing to do, and one reason not to. The additive half must not
        // be applied "while we are here".
        await ExecuteAsync(database, """
            DELETE FROM role_permission
             WHERE permission_id IN (SELECT id FROM permission WHERE code = 'user.read');
            DELETE FROM permission WHERE code = 'user.read';
            UPDATE permission SET is_active = false WHERE code = 'user.update';
            """);

        var result = await SynchroniseAsync(database);

        Assert.Equal(CatalogueSyncOutcome.Refused, result.Outcome);
        Assert.Equal(CatalogueSyncCounts.None, result.Counts);

        Assert.Equal(
            0,
            await CountAsync(database, "SELECT count(*) FROM permission WHERE code = 'user.read'"));
    }

    // ---------------------------------------------------------------- audit

    /// <summary>A12, A13 — one event per synchronisation, counts as committed.</summary>
    [Fact]
    public async Task A_successful_run_records_one_event_carrying_what_it_committed()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database, """
            DELETE FROM role_permission
             WHERE permission_id IN (SELECT id FROM permission WHERE code = 'user.read');
            DELETE FROM permission WHERE code = 'user.read';
            """);

        await SynchroniseAsync(database);

        Assert.Equal(1, await AuditRecordCountAsync(database));

        var payload = await ScalarAsync(database,
            "SELECT payload::text FROM audit.audit_record WHERE event_type = 'PermissionCatalogUpdated'");

        var document = JsonDocument.Parse(payload).RootElement;

        Assert.Equal("Succeeded", document.GetProperty("outcome").GetString());
        Assert.Equal(1, document.GetProperty("permissionsInserted").GetInt32());
        Assert.Equal(Release, document.GetProperty("release").GetString());
    }

    /// <summary>
    /// A12, A14. THE INVARIANT THE AUTONOMOUS WRITE PATH EXISTS FOR: the
    /// catalogue transaction rolled back and the evidence of the refusal did
    /// not go with it.
    /// </summary>
    [Fact]
    public async Task A_refused_run_records_its_refusal_with_reasons_and_a_digest()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database,
            "UPDATE permission SET is_active = false WHERE code = 'user.read'");

        var result = await SynchroniseAsync(database);

        Assert.Equal(1, await AuditRecordCountAsync(database));

        var payload = await ScalarAsync(database,
            "SELECT payload::text FROM audit.audit_record WHERE event_type = 'PermissionCatalogUpdated'");

        var document = JsonDocument.Parse(payload).RootElement;

        Assert.Equal("Refused", document.GetProperty("outcome").GetString());
        Assert.True(document.GetProperty("driftDetected").GetBoolean());
        Assert.Equal(
            "InactiveCatalogueEntry",
            document.GetProperty("refusalReasons")[0].GetProperty("reason").GetString());
        Assert.Equal(CatalogueSyncOutcome.Refused, result.Outcome);

        // The readable detail belongs to the operator, not the trail.
        Assert.DoesNotContain("user.read", payload);
    }

    /// <summary>
    /// Behaviour 14's secret scan rejects a hex digest of 40 characters or more
    /// and a high-entropy identifier, and a record that fails it is not written
    /// at all — so a digest of the findings and the database's name are both
    /// absent by contract, not by oversight.
    ///
    /// Asserted because the tempting "improvement" is to put a fingerprint back
    /// in, and the failure it causes appears only on a REFUSED run, which is
    /// the path least often exercised by hand.
    /// </summary>
    [Fact]
    public async Task The_payload_carries_neither_a_digest_nor_the_database_name()
    {
        await using var database = await ProvisionedAsync();

        await ExecuteAsync(database,
            "UPDATE permission SET is_active = false WHERE code = 'user.read'");

        await SynchroniseAsync(database);

        var payload = await ScalarAsync(database,
            "SELECT payload::text FROM audit.audit_record WHERE event_type = 'PermissionCatalogUpdated'");

        Assert.DoesNotContain("digest", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", payload, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------- helpers

    /// <summary>
    /// THE POSTCONDITION, and the same comparison CatalogueDriftTests makes
    /// against the shared database: every catalogue row the release declares is
    /// present, with the values it declares, and no extra.
    ///
    /// Asserted here rather than inferred from the counts. A synchroniser that
    /// reported "1 permission inserted" and inserted it wrong would satisfy
    /// every count assertion in this file and leave the database still drifted.
    /// </summary>
    private static async Task AssertConvergedAsync(AuditBoundaryDatabase database)
    {
        var permissions = await RowsAsync(database,
            "SELECT code, name, resource, action, requires_human_actor FROM permission WHERE is_active");

        Assert.Equal(
            PlatformProvisioner.GetPermissionSeeds()
                .Select(x => $"{x.Code}|{x.Name}|{x.Resource}|{x.Action}|{x.RequiresHumanActor}")
                .OrderBy(x => x, StringComparer.Ordinal),
            permissions);

        var roles = await RowsAsync(database,
            "SELECT code, name FROM role WHERE is_active");

        Assert.Equal(
            PlatformProvisioner.GetRoleSeeds()
                .Select(x => $"{x.Code}|{x.Name}")
                .OrderBy(x => x, StringComparer.Ordinal),
            roles);

        var grants = await RowsAsync(database, """
            SELECT r.code, p.code
              FROM role_permission rp
              JOIN role r       ON r.id = rp.role_id
              JOIN permission p ON p.id = rp.permission_id
             WHERE rp.revoked_at IS NULL
            """);

        Assert.Equal(
            PlatformProvisioner.GetRolePermissionSeeds()
                .Select(x => $"{x.RoleCode}|{x.PermissionCode}")
                .OrderBy(x => x, StringComparer.Ordinal),
            grants);
    }

    private static async Task<IReadOnlyList<string>> RowsAsync(
        AuditBoundaryDatabase database,
        string sql)
    {
        await using var connection = new NpgsqlConnection(database.PrivilegedConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<string>();

        while (await reader.ReadAsync())
        {
            rows.Add(string.Join(
                "|",
                Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i).ToString())));
        }

        return [.. rows.OrderBy(x => x, StringComparer.Ordinal)];
    }

    private static void AssertRefused(
        CatalogueSyncResult result,
        CatalogueRefusalReason reason,
        string subject)
    {
        Assert.Equal(CatalogueSyncOutcome.Refused, result.Outcome);
        Assert.Equal(CatalogueSyncCounts.None, result.Counts);
        Assert.Contains(new CatalogueRefusal(reason, subject), result.Refusals);
    }

    private static async Task<AuditBoundaryDatabase> ProvisionedAsync()
    {
        var database = await AuditBoundaryDatabase.CreateAsync();

        // The deployment phase, as Ligature.AuditSchema performs it: the event
        // catalogue exists before anything provisions or synchronises.
        await new AuditCatalogueDeployer(database.PrivilegedConnection).DeployAsync();

        await using var context = ContextFor(database, AuditBoundaryDatabase.ProvisioningRole);

        await new PlatformProvisioner(context)
            .ProvisionAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        return database;
    }

    private static async Task<CatalogueSyncResult> SynchroniseAsync(AuditBoundaryDatabase database)
    {
        await using var context = ContextFor(database, AuditBoundaryDatabase.MigrationRole);

        return await new CatalogueSynchroniser(
                context,
                database.ConnectionFor(AuditBoundaryDatabase.MigrationRole),
                Release)
            .SynchroniseAsync(DateTimeOffset.UtcNow, CancellationToken.None);
    }

    /// <summary>
    /// With the provenance interceptor, as every path that writes these tables
    /// has: UpdatedAt/UpdatedBy are stamped by it and attributed to the System
    /// actor when no execution context exists, which is the provisioning and
    /// synchronisation case.
    /// </summary>
    private static LigatureDbContext ContextFor(AuditBoundaryDatabase database, string role)
        => new(new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(database.ConnectionFor(role))
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);

    private static async Task ExecuteAsync(AuditBoundaryDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.PrivilegedConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> QueryAsync<T>(AuditBoundaryDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.PrivilegedConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static Task<long> CountAsync(AuditBoundaryDatabase database, string sql)
        => QueryAsync<long>(database, sql);

    private static Task<bool> BoolAsync(AuditBoundaryDatabase database, string sql)
        => QueryAsync<bool>(database, sql);

    private static Task<string> ScalarAsync(AuditBoundaryDatabase database, string sql)
        => QueryAsync<string>(database, sql);

    private static Task<long> AuditRecordCountAsync(AuditBoundaryDatabase database)
        => CountAsync(database,
            "SELECT count(*) FROM audit.audit_record WHERE event_type = 'PermissionCatalogUpdated'");
}
