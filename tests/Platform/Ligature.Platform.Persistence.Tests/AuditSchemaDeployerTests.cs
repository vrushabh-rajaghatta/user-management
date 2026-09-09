using Ligature.Platform.Persistence.Audit;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// "Did Ligature.AuditSchema build what it claims to build?"
///
/// The deployer verifies its own construction after applying the scripts and
/// fails the deployment if the boundary it claims is not the one that
/// exists. Every fixture that deploys already exercises the passing case;
/// these tests prove the FAILING one — that a defect the deployer did not
/// cause, but would otherwise hand on, stops it.
/// </summary>
public sealed class AuditSchemaDeployerTests
{
    [Fact]
    public async Task A_fresh_deployment_verifies_and_reports_every_script_current_on_rerun()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        var rerun = await new AuditSchemaDeployer(database.PrivilegedConnection).DeployAsync();

        Assert.Empty(rerun.Applied);
        Assert.Equal(AuditSchemaDeployer.ExpectedScriptNames, rerun.AlreadyCurrent);
    }

    /// <summary>
    /// The trigger mode is the mechanism that keeps a superuser in replica
    /// mode out of the trail. A trigger that is present but not ENABLE ALWAYS
    /// looks fine to every casual inspection, which is exactly why the tool
    /// must check it rather than trust it.
    /// </summary>
    [Fact]
    public async Task A_protection_trigger_that_is_not_ENABLE_ALWAYS_fails_the_deployment()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        await ExecuteAsync(database.PrivilegedConnection,
            "ALTER TABLE audit.audit_record ENABLE TRIGGER tg_audit_record_guard");

        var failure = await Assert.ThrowsAsync<AuditSchemaDeploymentException>(
            () => new AuditSchemaDeployer(database.PrivilegedConnection).DeployAsync());

        Assert.Contains("tg_audit_record_guard", failure.Message);
        Assert.Contains("ENABLE ALWAYS", failure.Message);
    }

    [Fact]
    public async Task A_grant_that_should_not_exist_fails_the_deployment()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        await ExecuteAsync(database.PrivilegedConnection,
            "GRANT DELETE ON audit.audit_record TO app_role");

        var failure = await Assert.ThrowsAsync<AuditSchemaDeploymentException>(
            () => new AuditSchemaDeployer(database.PrivilegedConnection).DeployAsync());

        Assert.Contains("app_role holds DELETE on audit.audit_record", failure.Message);
        Assert.Contains("DELETE on the trail is held by: app_role", failure.Message);
    }

    /// <summary>
    /// Every failing fact is reported, not the first. A deployment that
    /// stops with one reason and then stops again with the next is a
    /// deployment nobody can plan a fix for.
    /// </summary>
    [Fact]
    public async Task Every_failing_fact_is_reported_together()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        await ExecuteAsync(database.PrivilegedConnection,
            "ALTER TABLE audit.audit_retention_policy ENABLE TRIGGER tg_audit_retention_policy_frozen;"
            + "GRANT UPDATE ON audit.audit_entity_ref TO app_role");

        var failure = await Assert.ThrowsAsync<AuditSchemaDeploymentException>(
            () => new AuditSchemaDeployer(database.PrivilegedConnection).DeployAsync());

        Assert.Contains("tg_audit_retention_policy_frozen", failure.Message);
        Assert.Contains("app_role holds UPDATE on audit.audit_entity_ref", failure.Message);
    }

    [Fact]
    public async Task Missing_scripts_are_all_of_them_before_deployment_and_none_after()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(auditDeployed: false);

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            var before = await AuditSchemaDeployer.MissingScriptsAsync(
                connection, CancellationToken.None);

            Assert.Equal(AuditSchemaDeployer.ExpectedScriptNames, before);
        }

        await new AuditSchemaDeployer(database.ConnectionString).DeployAsync();

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            var after = await AuditSchemaDeployer.MissingScriptsAsync(
                connection, CancellationToken.None);

            Assert.Empty(after);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
