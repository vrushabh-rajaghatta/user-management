using SKSMCorp.Platform.Persistence.Audit;
using SKSMCorp.Platform.Persistence.Database;
using SKSMCorp.Platform.Persistence.Provisioning;
using SKSMCorp.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SKSMCorp.CatalogueSync.Tests;

/// <summary>
/// A throwaway database built the way an installation is built, in the same
/// order and with the same tools:
///
///     roles -> EF migrations -> audit schema -> audit catalogue -> provisioning
///
/// Each stage can be stopped short, because the tool's preconditions are about
/// exactly those gaps: a database that skipped the migrator, one that skipped
/// the audit schema, and one that was never provisioned are three different
/// states the tool must tell apart.
/// </summary>
internal sealed class SyncDatabase : IAsyncDisposable
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    /// <summary>
    /// Captured ONCE, before any test points SKSMCORP_CONNECTION at a throwaway
    /// database — which is how the tool is told where to write, so reading it
    /// lazily would make each test create its database inside the last one's.
    /// </summary>
    private static readonly string RootConnection =
        Environment.GetEnvironmentVariable("SKSMCORP_CONNECTION") ?? DefaultConnection;

    private readonly string _databaseName;

    private SyncDatabase(string databaseName, string connectionString)
    {
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    /// <summary>The fixture's own connection: owner rights, for arranging state.</summary>
    internal string ConnectionString { get; }

    /// <summary>
    /// What the TOOL is given, and it is deliberately not the one above.
    /// Catalogue synchronisation runs as migration_role in a deployment, so a
    /// fixture that handed it a superuser would exercise none of the privilege
    /// behaviour — a test revoking an audit grant would pass while the tool
    /// wrote the record anyway.
    ///
    /// The grants below are a fixture approximation of what the deployment
    /// gives migration_role: it owns the public schema there, having run the
    /// migrations. Here the tables are created by the owner connection and the
    /// rights are granted after the fact, which is close enough for what these
    /// tests assert and far closer than a superuser.
    /// </summary>
    internal string ToolConnection { get; private set; } = string.Empty;

    internal static async Task<SyncDatabase> CreateAsync(
        bool migrated = true,
        bool auditDeployed = true,
        bool provisioned = true)
    {
        var databaseName = $"sksmcorp_sync_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(For("postgres")))
        {
            try
            {
                await connection.OpenAsync();
            }
            catch (Exception failure)
                when (failure is NpgsqlException or System.Net.Sockets.SocketException)
            {
                throw new InvalidOperationException(
                    "PostgreSQL is unreachable, so this test cannot validate "
                    + "anything. See AGENTS.md section 3.",
                    failure);
            }

            await using var command = new NpgsqlCommand(
                $"CREATE DATABASE \"{databaseName}\"", connection);

            await command.ExecuteNonQueryAsync();
        }

        await TestRoles.EnsureAsync(For("postgres"));

        var database = new SyncDatabase(databaseName, For(databaseName));

        try
        {
            if (!migrated)
                return database;

            await using (var context = database.CreateContext())
                await context.Database.MigrateAsync();

            if (!auditDeployed)
                return database;

            await new AuditSchemaDeployer(database.ConnectionString).DeployAsync();
            await new AuditCatalogueDeployer(database.ConnectionString).DeployAsync();

            if (provisioned)
            {
                await using var context = database.CreateContext();

                await new PlatformProvisioner(context)
                    .ProvisionAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            }
        }
        catch
        {
            await database.DisposeAsync();

            throw;
        }
        finally
        {
            await database.GrantToolRightsAsync(migrated);
        }

        return database;
    }

    /// <summary>
    /// What migration_role holds in a deployment, arranged after the fact. The
    /// audit grants are NOT repeated here: 005 gives them, and a fixture that
    /// re-granted them would hide a missing 005 and make the audit-failure
    /// test unable to take them away.
    /// </summary>
    private async Task GrantToolRightsAsync(bool migrated)
    {
        ToolConnection = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = "migration_role",
            Password = TestRoles.Password,
        }.ToString();

        if (!migrated)
            return;

        await ExecuteAsync("""
            GRANT USAGE ON SCHEMA public TO migration_role;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO migration_role;
            """);
    }

    internal SKSMCorpDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SKSMCorpDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options);

    internal async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<T> QueryAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(For("postgres"));
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);

        await command.ExecuteNonQueryAsync();
    }

    private static string For(string database)
        => new NpgsqlConnectionStringBuilder(RootConnection) { Database = database }.ToString();
}
