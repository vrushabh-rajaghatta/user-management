using Ligature.Platform.Persistence.Audit;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// A throwaway database carrying the User Management schema AND the Audit
/// tamper boundary, built the way a customer installation is built:
///
///     roles  ->  EF migrations (migration_role)  ->  AuditSchemaDeployer
///
/// The order matters and is not a fixture convenience: the Audit foreign keys
/// reference app_user, role and user_role (AR10, AR12), so the User
/// Management schema has to exist first.
///
/// Why not the shared database. These tests connect as several different
/// roles and assert that most of them are refused. That needs a database
/// whose grants were applied by the deployer under test, not one a developer
/// provisioned by hand — and it must be safe for a test to attempt a DROP
/// TABLE, which is one of the things being proved impossible.
///
/// Roles are CLUSTER-scoped, not database-scoped, so creation is guarded and
/// the roles are deliberately left behind on dispose: a concurrent throwaway
/// database in the same cluster may still be using them. They carry no
/// privileges outside the databases that granted them any.
/// </summary>
internal sealed class AuditBoundaryDatabase : IAsyncDisposable
{
    /// <summary>
    /// A password for roles that exist only inside a developer's throwaway
    /// test database. It is not a deployment credential: nothing outside this
    /// fixture ever authenticates with it, and the databases it reaches are
    /// dropped when the test finishes. Real deployments supply role passwords
    /// through configuration with no default (docs/architecture.md section 17).
    /// </summary>
    private const string TestRolePassword = "ligature-test-role";

    internal const string AppRole = "app_role";
    internal const string MigrationRole = "migration_role";
    internal const string ProvisioningRole = "provisioning_role";
    internal const string AnonymiserRole = "audit_anonymiser";
    internal const string OwnerRole = "audit_owner";

    private readonly string _databaseName;
    private readonly string _maintenanceConnectionString;

    private AuditBoundaryDatabase(
        string databaseName,
        string maintenanceConnectionString)
    {
        _databaseName = databaseName;
        _maintenanceConnectionString = maintenanceConnectionString;
    }

    internal static async Task<AuditBoundaryDatabase> CreateAsync()
    {
        // Fails loudly rather than silently skipping, per AGENTS.md section 3.
        await TestDatabase.EnsureReachableAsync();

        var databaseName = $"ligature_audit_{Guid.NewGuid():N}";

        var maintenance = new NpgsqlConnectionStringBuilder(
            TestDatabase.ConnectionString)
        {
            Database = "postgres",
        }.ConnectionString;

        await EnsureRolesAsync(maintenance);

        await using (var connection = new NpgsqlConnection(maintenance))
        {
            await connection.OpenAsync();

            // Owned by migration_role on purpose. That is the arrangement a
            // real deployment has, and it is precisely the arrangement under
            // which the audit tables must still be safe: a database owner is
            // the owner of the public schema, and a schema owner may drop
            // tables it does not own.
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{databaseName}\" OWNER {MigrationRole}",
                connection);

            await create.ExecuteNonQueryAsync();
        }

        var database = new AuditBoundaryDatabase(databaseName, maintenance);

        try
        {
            await database.ApplyUserManagementSchemaAsync();
            await database.GrantConnectAsync();
            await database.ApplyAuditSchemaAsync();
        }
        catch
        {
            await database.DisposeAsync();

            throw;
        }

        return database;
    }

    /// <summary>A connection string for the named role against this database.</summary>
    internal string ConnectionFor(string role)
        => new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString)
        {
            Database = _databaseName,
            Username = role,
            Password = TestRolePassword,
        }.ConnectionString;

    /// <summary>
    /// The maintenance connection, which on a developer machine is a
    /// superuser. Used by the tests that assert what a superuser still cannot
    /// do, and to set up fixture rows.
    /// </summary>
    internal string PrivilegedConnection
        => new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString)
        {
            Database = _databaseName,
        }.ConnectionString;

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        await using var connection =
            new NpgsqlConnection(_maintenanceConnectionString);

        await connection.OpenAsync();

        await using (var terminate = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(pid) FROM pg_stat_activity
            WHERE datname = @name AND pid <> pg_backend_pid()
            """, connection))
        {
            terminate.Parameters.AddWithValue("name", _databaseName);

            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{_databaseName}\"", connection);

        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Guarded because roles are cluster-scoped: a second throwaway database
    /// in the same cluster would otherwise collide on CREATE ROLE.
    /// audit_owner and audit_anonymiser are NOT created here — the deployer
    /// owns them, and a fixture that pre-created them would hide a failure to
    /// do so.
    /// </summary>
    private static async Task EnsureRolesAsync(string maintenance)
    {
        await using var connection = new NpgsqlConnection(maintenance);

        await connection.OpenAsync();

        foreach (var role in new[] { AppRole, MigrationRole, ProvisioningRole })
        {
            await using var command = new NpgsqlCommand(
                $"""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                        CREATE ROLE {role} LOGIN PASSWORD '{TestRolePassword}';
                    END IF;
                END $$;
                """, connection);

            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task ApplyUserManagementSchemaAsync()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(ConnectionFor(MigrationRole))
            .Options;

        await using var context = new LigatureDbContext(options);

        // The real migrations, so the boundary sits on the schema a customer
        // would have.
        await context.Database.MigrateAsync();
    }

    private async Task GrantConnectAsync()
    {
        await using var connection = new NpgsqlConnection(PrivilegedConnection);

        await connection.OpenAsync();

        foreach (var role in new[] { AppRole, ProvisioningRole })
        {
            await using var command = new NpgsqlCommand(
                $"GRANT CONNECT ON DATABASE \"{_databaseName}\" TO {role};"
                + $"GRANT USAGE ON SCHEMA public TO {role};",
                connection);

            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task ApplyAuditSchemaAsync()
        => await new AuditSchemaDeployer(PrivilegedConnection).DeployAsync();
}
