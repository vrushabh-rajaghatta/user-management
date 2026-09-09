using Ligature.Platform.Persistence.Audit;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Provisioning.Tests;

/// <summary>
/// A database created for one test and dropped afterwards, optionally left
/// unmigrated so the tool's schema check can be exercised.
///
/// The shared development database cannot be used: it is already provisioned,
/// so every sentinel would take its "already present" branch and the tool's
/// actual first-run behaviour would go untested.
/// </summary>
internal sealed class ProvisioningDatabase : IAsyncDisposable
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres";

    private readonly string _databaseName;

    private ProvisioningDatabase(string databaseName, string connectionString)
    {
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    internal string ConnectionString { get; }

    /// <summary>
    /// Captured ONCE, at type initialisation, before any test has set
    /// LIGATURE_CONNECTION to point at a throwaway database. These tests must
    /// change that variable — it is how the tool is told where to write — so
    /// reading it lazily would make the harness follow whatever the last test
    /// set, and a second test would try to create its database inside the
    /// first one's. No new environment variable is introduced for this.
    /// </summary>
    private static readonly string RootConnection =
        Environment.GetEnvironmentVariable("LIGATURE_CONNECTION")
        ?? DefaultConnection;

    /// <param name="migrated">Apply the EF migrations.</param>
    /// <param name="auditDeployed">
    /// Also deploy the audit schema, as an installation does after migrating.
    /// False exercises the tool's second precondition: migrated, but the
    /// audit schema absent.
    /// </param>
    internal static async Task<ProvisioningDatabase> CreateAsync(
        bool migrated,
        bool auditDeployed = true)
    {
        var databaseName = $"ligature_prov_{Guid.NewGuid():N}";

        var maintenance = For("postgres");
        var target = For(databaseName);

        await using (var connection = new NpgsqlConnection(maintenance))
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

        // Before migrating: AddUserManagementPrivilegeModel grants to
        // app_role and provisioning_role, and a GRANT to a missing role
        // fails. A customer installation runs the roles foundation step
        // before the migrator; this is the fixture doing the same.
        await TestRoles.EnsureAsync(For("postgres"));

        var database = new ProvisioningDatabase(databaseName, target);

        if (!migrated)
            return database;

        try
        {
            await using (var context = database.CreateContext())
            {
                await context.Database.MigrateAsync();
            }

            if (auditDeployed)
                await new AuditSchemaDeployer(target).DeployAsync();
        }
        catch
        {
            await database.DisposeAsync();

            throw;
        }

        return database;
    }

    internal LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new SystemClock(), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    internal async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(For("postgres"));

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

    private static string For(string databaseName)
        => new NpgsqlConnectionStringBuilder(RootConnection)
        {
            Database = databaseName,
        }.ConnectionString;
}
