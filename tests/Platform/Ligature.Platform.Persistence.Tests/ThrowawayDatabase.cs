using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// A migrated but otherwise EMPTY database, created for one test and dropped
/// afterwards.
///
/// Provisioning cannot be tested against the shared database. That one is
/// already provisioned, so PRV-C1 and PRV-C3 would only ever take their
/// "already present" branches — which is precisely how their first-provision
/// paths came to have no execution coverage at all despite the suite being
/// green. Every other test class in this assembly reads and writes the shared
/// database; these tests must not, in either direction.
/// </summary>
internal sealed class ThrowawayDatabase : IAsyncDisposable
{
    private readonly string _databaseName;
    private readonly string _maintenanceConnectionString;

    private ThrowawayDatabase(
        string databaseName,
        string maintenanceConnectionString,
        string connectionString)
    {
        _databaseName = databaseName;
        _maintenanceConnectionString = maintenanceConnectionString;
        ConnectionString = connectionString;
    }

    internal string ConnectionString { get; }

    internal static async Task<ThrowawayDatabase> CreateAsync()
    {
        // Fails loudly rather than silently skipping, per AGENTS.md section 3.
        await TestDatabase.EnsureReachableAsync();

        // A fresh name per test, so a crashed run cannot poison the next one
        // and two runs cannot collide.
        var databaseName = $"ligature_prov_{Guid.NewGuid():N}";

        var maintenance = Rebuild("postgres");
        var target = Rebuild(databaseName);

        // AddUserManagementPrivilegeModel grants to app_role and
        // provisioning_role, and a GRANT to a missing role fails. A customer
        // installation runs the roles foundation step before the migrator;
        // this is the fixture doing the same, so the suite works on a clean
        // machine rather than only where a previous run left the roles behind.
        await TestRoles.EnsureAsync(maintenance);

        await using (var connection = new NpgsqlConnection(maintenance))
        {
            await connection.OpenAsync();

            // The name is a generated identifier, not caller input, and
            // CREATE DATABASE cannot be parameterised.
            await using var command = new NpgsqlCommand(
                $"CREATE DATABASE \"{databaseName}\"", connection);

            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await using var context = Context(target);

            // The real migrations, so what is provisioned sits on the same
            // schema production would have.
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DropAsync(maintenance, databaseName);

            throw;
        }

        return new ThrowawayDatabase(databaseName, maintenance, target);
    }

    /// <summary>
    /// A context on this database, with the provenance interceptor the
    /// provisioners depend on — app_user's UpdatedAt/UpdatedBy are shadow
    /// properties, and nothing writes them otherwise.
    /// </summary>
    internal LigatureDbContext CreateContext() => Context(ConnectionString);

    public async ValueTask DisposeAsync()
        => await DropAsync(_maintenanceConnectionString, _databaseName);

    private static LigatureDbContext Context(string connectionString)
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new SystemClock(), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    private static string Rebuild(string databaseName)
        => new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;

    /// <summary>
    /// Pooled connections outlive the context that opened them, and PostgreSQL
    /// refuses to drop a database that still has any. Backends are terminated
    /// explicitly rather than relying on DROP ... WITH (FORCE), which needs
    /// PostgreSQL 13.
    /// </summary>
    private static async Task DropAsync(string maintenance, string databaseName)
    {
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(maintenance);

        await connection.OpenAsync();

        await using (var terminate = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(pid) FROM pg_stat_activity
            WHERE datname = @name AND pid <> pg_backend_pid()
            """, connection))
        {
            terminate.Parameters.AddWithValue("name", databaseName);

            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);

        await drop.ExecuteNonQueryAsync();
    }
}
