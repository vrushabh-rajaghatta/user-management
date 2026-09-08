using System.Net.Sockets;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The persistence suite is the evidence that the adapters work, so a run
/// without PostgreSQL must be loud.
///
/// These classes previously caught the connection failure and let the test body
/// return early, which xUnit reports as PASSED having asserted nothing — an
/// unreachable database silently became a green suite. xUnit 2.9.3 has no
/// dynamic Assert.Skip (that is a v3 feature), and upgrading the test framework
/// is a larger change than this story, so the remaining honest option is to
/// fail clearly. AGENTS.md section 3 permits exactly that.
/// </summary>
internal static class TestDatabase
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("LIGATURE_CONNECTION")
        ?? DefaultConnection;

    /// <summary>
    /// Throws with a message naming the cause and the fix, rather than
    /// returning a bool a caller could quietly ignore.
    /// </summary>
    internal static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);

        try
        {
            await connection.OpenAsync();

            return connection;
        }
        catch (Exception failure) when (
            failure is NpgsqlException or SocketException)
        {
            await connection.DisposeAsync();

            throw new InvalidOperationException(
                "PostgreSQL is unreachable, so this integration test cannot "
                + "validate anything. Set LIGATURE_CONNECTION or start the "
                + "database at the default host, then re-run. See AGENTS.md "
                + "section 3.",
                failure);
        }
    }

    /// <summary>
    /// Asserts only that the database can be reached, for tests that need no
    /// seed data.
    /// </summary>
    internal static async Task EnsureReachableAsync()
    {
        await using var connection = await OpenAsync();
    }

    /// <summary>
    /// Schema alone is not enough: the seed data comes from
    /// PlatformProvisioner, which has no entry point yet, so a fresh database
    /// must be provisioned out of band (docs/requirements.md, Known Gaps).
    /// </summary>
    internal static async Task EnsureProvisionedAsync()
    {
        await using var connection = await OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM app_user WHERE actor_type = 'System'",
            connection);

        if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0)
        {
            throw new InvalidOperationException(
                "The target database has no System actor, so it has not been "
                + "provisioned by PRV-C1 and this test cannot validate "
                + "anything. Provision it out of band first.");
        }
    }
}
