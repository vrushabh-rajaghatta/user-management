using System.Net.Sockets;
using Npgsql;

namespace Ligature.Host.Tests;

/// <summary>
/// The Host suite is the evidence that the HTTP boundary works end to end, so
/// a run without PostgreSQL must be loud rather than green.
///
/// Deliberately a copy of the Persistence suite's helper rather than a shared
/// one: making it shared would mean either a test project referencing another
/// test project, or a production project carrying test infrastructure. Both are
/// worse than thirty duplicated lines. AGENTS.md section 3 permits failing
/// clearly, which is what this does.
/// </summary>
internal static class TestDatabase
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres";

    /// <summary>
    /// The host itself has NO fallback — it refuses to start without
    /// LIGATURE_CONNECTION. The fallback here is a developer-machine
    /// convenience, and the factory supplies the resulting value to the host
    /// explicitly, which is the distinction the host is making.
    /// </summary>
    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("LIGATURE_CONNECTION")
        ?? DefaultConnection;

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
    /// Schema alone is not enough: sign-in resolves the effective security
    /// policy, which PlatformProvisioner seeds and which has no entry point
    /// yet (docs/requirements.md, Known Gaps).
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
