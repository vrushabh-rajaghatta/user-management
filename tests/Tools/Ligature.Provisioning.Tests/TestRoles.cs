using Npgsql;

namespace Ligature.Provisioning.Tests;

/// <summary>
/// Ensures the general database roles exist before any migration runs.
///
/// AddUserManagementPrivilegeModel grants to app_role and provisioning_role,
/// and PostgreSQL refuses a GRANT to a role that does not exist. A customer
/// installation runs the roles foundation step before the migrator; a fixture
/// that migrates has to do the same, or the suite passes only on a machine
/// where some earlier run happened to leave the roles behind.
///
/// docker/roles.sql is the canonical definition. This is a deliberate copy of
/// the equivalent helper in the Persistence test assembly: the two test
/// projects do not reference one another, and a shared test-support project
/// for three statements would cost more than the repetition.
/// </summary>
internal static class TestRoles
{
    private const string Password = "ligature-test-role";

    private static readonly string[] All =
        ["app_role", "migration_role", "provisioning_role"];

    internal static async Task EnsureAsync(string maintenanceConnectionString)
    {
        await using var connection =
            new NpgsqlConnection(maintenanceConnectionString);

        await connection.OpenAsync();

        foreach (var role in All)
        {
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
