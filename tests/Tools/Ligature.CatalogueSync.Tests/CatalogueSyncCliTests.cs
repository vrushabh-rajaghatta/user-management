namespace Ligature.CatalogueSync.Tests;

/// <summary>
/// The tool's contract with whoever runs a deployment (docs/requirements.md,
/// PRV-C2).
///
/// The distinction these tests exist for is REFUSED versus FAILED. A refusal
/// means the tool successfully determined that the database violates the
/// reconciliation policy — an expected, actionable deployment outcome. A
/// failure means it could not determine anything. Collapsing them would make a
/// deployment pipeline treat a policy decision as an outage, or an outage as a
/// policy decision.
/// </summary>
[Collection(nameof(CatalogueSyncCliTests))]
[CollectionDefinition(nameof(CatalogueSyncCliTests), DisableParallelization = true)]
public sealed class CatalogueSyncCliTests
{
    // ----------------------------------------------------------- exit codes

    [Fact]
    public async Task A_database_already_current_succeeds()
    {
        await using var database = await SyncDatabase.CreateAsync();

        var run = await RunAsync(database);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("Catalogue synchronisation succeeded.", run.Output);
    }

    /// <summary>
    /// The clean-bootstrap path. ./up.sh reaches a database with schema and no
    /// System actor, so this must be a success — not a refusal, and not a
    /// failure.
    /// </summary>
    [Fact]
    public async Task An_unprovisioned_database_succeeds_as_a_no_op()
    {
        await using var database = await SyncDatabase.CreateAsync(provisioned: false);

        var run = await RunAsync(database);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("No System actor", run.Output);
        Assert.Contains("Nothing was changed and nothing was recorded.", run.Output);
    }

    [Fact]
    public async Task A_run_that_converges_reports_what_it_added()
    {
        await using var database = await SyncDatabase.CreateAsync();

        await database.ExecuteAsync("""
            DELETE FROM role_permission
             WHERE permission_id IN (SELECT id FROM permission WHERE code = 'user.read');
            DELETE FROM permission WHERE code = 'user.read';
            """);

        var run = await RunAsync(database);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Contains("Permissions added:              1", run.Output);
    }

    /// <summary>
    /// 2, not 3. And the subject-level detail is HERE, where whoever has to fix
    /// it is standing — the audit payload carries only reason codes and counts.
    /// </summary>
    [Fact]
    public async Task Drift_the_release_may_not_resolve_is_refused_with_its_subjects()
    {
        await using var database = await SyncDatabase.CreateAsync();

        await database.ExecuteAsync(
            "UPDATE permission SET is_active = false WHERE code = 'user.read'");

        var run = await RunAsync(database);

        Assert.True(run.ExitCode == 2, run.Error);
        Assert.Contains("Catalogue synchronisation refused.", run.Error);
        Assert.Contains("InactiveCatalogueEntry: user.read", run.Error);
        Assert.Contains("No catalogue mutations were committed.", run.Error);
    }

    [Fact]
    public async Task Arguments_are_a_usage_error()
    {
        await using var database = await SyncDatabase.CreateAsync();

        var run = await RunAsync(database, "--force");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("takes no arguments", run.Error);
    }

    [Fact]
    public async Task An_absent_connection_setting_is_a_usage_error()
    {
        var previous = Environment.GetEnvironmentVariable("LIGATURE_CONNECTION");

        try
        {
            Environment.SetEnvironmentVariable("LIGATURE_CONNECTION", null);

            var run = await RunAsync(connectionString: null);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("will not guess which database", run.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIGATURE_CONNECTION", previous);
        }
    }

    // --------------------------------------------------------- preconditions

    [Fact]
    public async Task Outstanding_migrations_are_an_operational_failure()
    {
        await using var database = await SyncDatabase.CreateAsync(migrated: false);

        var run = await RunAsync(database);

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("pending migration", run.Error);
        Assert.Contains("dotnet ef database update", run.Error);
    }

    /// <summary>
    /// The audit schema carries 005, which grants this tool's role the INSERT it
    /// needs, and the corrected event definition. Reconciling without it would
    /// commit and then be unable to record what it did.
    /// </summary>
    [Fact]
    public async Task An_undeployed_audit_schema_is_an_operational_failure()
    {
        await using var database = await SyncDatabase.CreateAsync(auditDeployed: false);

        var run = await RunAsync(database);

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("Audit schema is not deployed", run.Error);
        Assert.Contains("Ligature.AuditSchema", run.Error);
    }

    // -------------------------------------------------------- audit failure

    /// <summary>
    /// THE FAILURE PATH THAT MATTERS, and the one hardest to reach by hand: the
    /// catalogue committed and its record did not.
    ///
    /// Injected by revoking the INSERT that 005 grants, which is a state a
    /// misconfigured deployment can genuinely reach. Nothing is compensated —
    /// the transaction is already committed, and inventing reversal SQL would
    /// turn a reporting failure into an authorization change nobody asked for.
    /// </summary>
    [Fact]
    public async Task A_committed_run_whose_audit_write_fails_reports_it_and_changes_nothing_back()
    {
        await using var database = await SyncDatabase.CreateAsync();

        await database.ExecuteAsync("""
            DELETE FROM role_permission
             WHERE permission_id IN (SELECT id FROM permission WHERE code = 'user.read');
            DELETE FROM permission WHERE code = 'user.read';
            REVOKE INSERT ON audit.audit_record FROM migration_role;
            """);

        var run = await RunAsync(database);

        Assert.Equal(3, run.ExitCode);

        // 1. the catalogue change is committed
        Assert.Equal(
            1L,
            await database.QueryAsync<long>(
                "SELECT count(*) FROM permission WHERE code = 'user.read'"));

        // 2. no audit record
        Assert.Equal(
            0L,
            await database.QueryAsync<long>(
                "SELECT count(*) FROM audit.audit_record WHERE event_type = 'PermissionCatalogUpdated'"));

        // 3 and 4. it says which, and that nothing was undone
        Assert.Contains("SUCCEEDED, and its audit record could not be written", run.Error);
        Assert.Contains("NOT reversed", run.Error);
        Assert.Contains("No compensation was attempted.", run.Error);
    }

    /// <summary>
    /// The refusal variant, and the reason the message is built from the
    /// outcome. Nothing was committed here, so an operator told that catalogue
    /// changes are applied and unreversed would go looking for damage that does
    /// not exist.
    /// </summary>
    [Fact]
    public async Task A_refused_run_whose_audit_write_fails_says_nothing_was_committed()
    {
        await using var database = await SyncDatabase.CreateAsync();

        await database.ExecuteAsync("""
            UPDATE permission SET is_active = false WHERE code = 'user.read';
            REVOKE INSERT ON audit.audit_record FROM migration_role;
            """);

        var run = await RunAsync(database);

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("REFUSED, and its audit record could not be written", run.Error);
        Assert.Contains("there is nothing to undo", run.Error);
        Assert.DoesNotContain("NOT reversed", run.Error);
    }

    // -------------------------------------------------------------- helpers

    private sealed record Run(int ExitCode, string Output, string Error);

    private static Task<Run> RunAsync(SyncDatabase database, params string[] args)
        => RunAsync(database.ToolConnection, args);

    private static async Task<Run> RunAsync(string? connectionString, params string[] args)
    {
        if (connectionString is not null)
            Environment.SetEnvironmentVariable("LIGATURE_CONNECTION", connectionString);

        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Program.RunAsync(args, output, error, CancellationToken.None);

        return new(code, output.ToString(), error.ToString());
    }
}
