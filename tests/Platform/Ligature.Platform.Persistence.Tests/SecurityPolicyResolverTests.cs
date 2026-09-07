using System.Net.Sockets;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Provisioning;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The resolver's correctness rests on two things a real database has to
/// confirm: that version selection is genuinely MAX(EffectiveFrom) &lt;= at, and
/// that the seeded 0001-01-01 sentinel — which Npgsql maps to PostgreSQL's
/// -infinity — round-trips and orders before every real timestamp. Neither is
/// observable without PostgreSQL.
///
/// Target database comes from LIGATURE_CONNECTION; the tests skip when no
/// database is reachable, matching CatalogueDriftTests.
/// </summary>
public sealed class SecurityPolicyResolverTests
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres";

    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 10, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Provisioning seeds version 1 from SecurityBaseline.Current, so a
    /// provisioned database with no tenant overrides must resolve to the
    /// baseline exactly. This is also the test that would catch the provisioner
    /// and the baseline drifting apart.
    /// </summary>
    [Fact]
    public async Task A_provisioned_database_resolves_to_the_seeded_baseline()
    {
        if (!await IsReachableAsync())
            return;

        await using var context = CreateContext();
        var resolver = new SecurityPolicyResolver(context);

        if (!await IsProvisionedAsync())
            return;

        var effective = await resolver.GetEffectiveSettingsAsync(
            Now, CancellationToken.None);

        Assert.Equal(SecurityBaseline.Current, effective);
    }

    /// <summary>
    /// The seed and the baseline must be the same values — that is what makes
    /// "a new tenant starts at exactly the standard it is evaluated against"
    /// true rather than aspirational.
    ///
    /// This has to be asserted against the code rather than the database.
    /// An already-provisioned database keeps whatever it was seeded with, so if
    /// the provisioner were changed to seed something else, no query against
    /// the deployed row would notice — only newly provisioned tenants would
    /// diverge, and by then the drift is in production.
    /// </summary>
    [Fact]
    public void The_provisioner_seeds_from_the_baseline()
    {
        Assert.Same(
            SecurityBaseline.Current,
            PlatformProvisioner.GetInitialSecurityPolicySeed());
    }

    /// <summary>
    /// The seeded version uses 0001-01-01, which Npgsql stores as -infinity.
    /// The resolver's WHERE EffectiveFrom &lt;= at depends on that comparing
    /// before every real timestamp; if it did not, a provisioned database would
    /// resolve to nothing and every command would throw.
    /// </summary>
    [Fact]
    public async Task The_seeded_sentinel_orders_before_every_real_timestamp()
    {
        if (!await IsReachableAsync() || !await IsProvisionedAsync())
            return;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT effective_from = '-infinity'::timestamptz,
                   effective_from <= now()
            FROM security_policy
            WHERE policy_version = 1
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "Version 1 is missing.");

        Assert.True(
            reader.GetBoolean(0),
            "The seeded 0001-01-01 did not round-trip as -infinity.");

        Assert.True(
            reader.GetBoolean(1),
            "-infinity does not order before now().");
    }

    /// <summary>
    /// A newer version supersedes an older one once its effective date passes,
    /// and not before — SP1's deterministic resolution.
    /// </summary>
    [Fact]
    public async Task The_newest_version_whose_date_has_passed_wins()
    {
        if (!await IsReachableAsync() || !await IsProvisionedAsync())
            return;

        await using var context = CreateContext();
        var resolver = new SecurityPolicyResolver(context);

        // Deliberately stronger than the baseline in both directions, so the
        // assertion cannot pass by accidentally reading the seeded row.
        var stricter = SecurityBaseline.Current with
        {
            PasswordMinLength = 20,
            SessionIdleTimeout = TimeSpan.FromMinutes(5),
        };

        var effectiveFrom = Now.AddYears(1);

        var version = await InsertVersionAsync(context, stricter, effectiveFrom);

        try
        {
            var before = await resolver.GetEffectiveSettingsAsync(
                effectiveFrom.AddSeconds(-1), CancellationToken.None);

            Assert.Equal(SecurityBaseline.Current, before);

            var after = await resolver.GetEffectiveSettingsAsync(
                effectiveFrom.AddSeconds(1), CancellationToken.None);

            Assert.Equal(20, after.PasswordMinLength);
            Assert.Equal(TimeSpan.FromMinutes(5), after.SessionIdleTimeout);
        }
        finally
        {
            await DeleteVersionAsync(version);
        }
    }

    /// <summary>
    /// Inv. 31's whole point: a stored value that weakens the baseline is
    /// clamped at EVALUATION time. The row keeps whatever the tenant wrote —
    /// the resolver never writes — but the enforced answer is the baseline.
    /// </summary>
    [Fact]
    public async Task A_stored_value_weaker_than_the_baseline_is_clamped_without_being_rewritten()
    {
        if (!await IsReachableAsync() || !await IsProvisionedAsync())
            return;

        await using var context = CreateContext();
        var resolver = new SecurityPolicyResolver(context);

        var weakened = SecurityBaseline.Current with
        {
            PasswordMinLength = 4,
            SessionIdleTimeout = TimeSpan.FromHours(8),
        };

        var effectiveFrom = Now.AddYears(2);

        var version = await InsertVersionAsync(context, weakened, effectiveFrom);

        try
        {
            var effective = await resolver.GetEffectiveSettingsAsync(
                effectiveFrom.AddSeconds(1), CancellationToken.None);

            Assert.Equal(
                SecurityBaseline.Current.PasswordMinLength,
                effective.PasswordMinLength);

            Assert.Equal(
                SecurityBaseline.Current.SessionIdleTimeout,
                effective.SessionIdleTimeout);

            // The stored row is untouched: the tenant's configuration survives
            // exactly as written, and only the computed answer differs.
            var (storedMinLength, storedIdle) = await ReadStoredAsync(version);

            Assert.Equal(4, storedMinLength);
            Assert.Equal(TimeSpan.FromHours(8), storedIdle);
        }
        finally
        {
            await DeleteVersionAsync(version);
        }
    }

    /// <summary>
    /// Emptying the table is the only way to reach this branch. Choosing an
    /// early instant does not work — the seeded version is -infinity, and
    /// nothing sorts before that, which is itself worth knowing.
    ///
    /// The delete runs inside a transaction that is always rolled back, so the
    /// shared database is never actually altered; the resolver reads the
    /// uncommitted state through the same context.
    /// </summary>
    [Fact]
    public async Task An_unprovisioned_database_fails_loudly()
    {
        if (!await IsReachableAsync() || !await IsProvisionedAsync())
            return;

        await using var context = CreateContext();
        var resolver = new SecurityPolicyResolver(context);

        await using var transaction = await context.Database
            .BeginTransactionAsync(CancellationToken.None);

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM security_policy", CancellationToken.None);

        var failure = await Assert.ThrowsAsync<ProvisioningException>(
            () => resolver.GetEffectiveSettingsAsync(
                Now, CancellationToken.None));

        Assert.Contains(
            "provisioned", failure.Message, StringComparison.OrdinalIgnoreCase);

        await transaction.RollbackAsync(CancellationToken.None);

        // The rollback must genuinely restore the seeded policy.
        Assert.True(
            await IsProvisionedAsync(),
            "The test emptied security_policy without restoring it.");
    }

    private static async Task<SecurityPolicyId> InsertVersionAsync(
        LigatureDbContext context,
        SecurityPolicySettings settings,
        DateTimeOffset effectiveFrom)
    {
        var nextVersion = await NextPolicyVersionAsync();

        var policy = SecurityPolicy.Create(
            SecurityPolicyId.New(),
            nextVersion,
            effectiveFrom,
            settings,
            Now,
            User.SystemUserId);

        context.Add(policy);
        await context.SaveChangesAsync(CancellationToken.None);

        return policy.Id;
    }

    private static async Task<int> NextPolicyVersionAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT coalesce(max(policy_version), 0) + 1 FROM security_policy",
            connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<(int MinLength, TimeSpan Idle)> ReadStoredAsync(
        SecurityPolicyId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT password_min_length, session_idle_timeout
            FROM security_policy WHERE id = @id
            """, connection);

        command.Parameters.AddWithValue("id", id.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "The policy row is missing.");

        return (reader.GetInt32(0), reader.GetFieldValue<TimeSpan>(1));
    }

    /// <summary>
    /// SP2 makes security_policy append-only for the application role; these
    /// rows are test residue and the test connection is not that role.
    /// </summary>
    private static async Task DeleteVersionAsync(SecurityPolicyId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "DELETE FROM security_policy WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", id.Value);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsProvisionedAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM security_policy", connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new FixedClock(Now),
                    executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("LIGATURE_CONNECTION")
        ?? DefaultConnection;

    private static async Task<bool> IsReachableAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);

        try
        {
            await connection.OpenAsync();

            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
