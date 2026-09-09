using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// Ensures the general database roles exist before any migration runs.
///
/// AddUserManagementPrivilegeModel grants to app_role and provisioning_role,
/// and PostgreSQL refuses a GRANT to a role that does not exist. Every fixture
/// that migrates therefore has to establish them first, exactly as a customer
/// installation runs the roles foundation step before the migrator.
///
/// docker/roles.sql is the canonical definition; this is its test-time
/// equivalent, deliberately reduced to what the fixtures need. It is repeated
/// in the Provisioning test assembly rather than shared, because the two test
/// projects do not reference one another and a test-support project for three
/// statements would be worse than the repetition.
///
/// Roles are CLUSTER-scoped, so creation is guarded and they are left behind
/// on dispose: a concurrent throwaway database may still be using them, and
/// they hold no privileges outside databases that granted them some.
/// </summary>
internal static class TestRoles
{
    /// <summary>
    /// For roles that exist only inside a developer's throwaway test
    /// databases. Not a deployment credential: real deployments supply role
    /// passwords through configuration with no default
    /// (docs/architecture.md section 17).
    /// </summary>
    internal const string Password = "ligature-test-role";

    internal const string App = "app_role";
    internal const string Migration = "migration_role";
    internal const string Provisioning = "provisioning_role";

    private static readonly string[] All = [App, Migration, Provisioning];

    internal static async Task EnsureAsync(string maintenanceConnectionString)
    {
        await using var connection =
            new NpgsqlConnection(maintenanceConnectionString);

        await connection.OpenAsync();

        foreach (var role in All)
        {
            // Guarded rather than DROP-and-recreate: another test's database
            // in the same cluster may already have granted to it.
            await using var command = new NpgsqlCommand(
                $"""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                        CREATE ROLE {role} LOGIN PASSWORD '{Password}';
                    END IF;
                END $$;
                """, connection);

            await command.ExecuteNonQueryAsync();
        }
    }
}
