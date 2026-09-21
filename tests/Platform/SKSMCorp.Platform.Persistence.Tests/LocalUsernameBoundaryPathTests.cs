using SKSMCorp.Platform.Application;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Users.Commands.CreateUser;
using SKSMCorp.Platform.Application.Users.Queries.UsernameAvailability;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using SKSMCorp.Platform.Persistence.Provisioning;
using SKSMCorp.Platform.Persistence.Repositories;
using SKSMCorp.Platform.Persistence.Services;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// THE INVARIANT (docs/requirements.md, "Local usernames refuse surrounding
/// whitespace", UW-3 to UW-5): every path that creates or changes a local
/// username applies the same domain rule, and refuses with the same sentence.
///
///     UserIdentity.CreateLocal, UserIdentity.ChangeUsername (IDN-C2 to come),
///     USR-C1, IDN-Q3, the PRV-C3 provisioner
///
/// The provisioning CLI refuses the same values before it touches a database;
/// that half is in SKSMCorp.Provisioning.Tests (UW-10). The CHECK backing all
/// of them is LocalUsernameConstraintTests.
///
/// An invalid username is refused BEFORE any uniqueness lookup (WS5, WS6): a
/// recording repository proves ExistsWithUsernameAsync is never asked.
/// </summary>
public sealed class LocalUsernameBoundaryPathTests : IClassFixture<ActivationDatabase>
{
    internal const string Refusal = "A username cannot begin or end with whitespace.";

    private const string Blank = "Username cannot be empty.";

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly ActivationDatabase _database;

    public LocalUsernameBoundaryPathTests(ActivationDatabase database)
        => _database = database;

    /// <summary>UW-3's shared set: space, tab, LF, U+00A0, U+2028 and U+3000, each leading and trailing.</summary>
    public static TheoryData<string> SpacedUsernames()
    {
        var data = new TheoryData<string>();

        foreach (var whitespace in new[] { ' ', '\t', '\n', '\u00A0', '\u2028', '\u3000' })
        {
            data.Add($"{whitespace}boundary");
            data.Add($"boundary{whitespace}");
        }

        return data;
    }

    // ---------------------------------------------------------------- UW-3

    [Theory]
    [MemberData(nameof(SpacedUsernames))]
    public void The_domain_refuses_it_on_create_and_on_change(string username)
    {
        AssertRefused(() => UserIdentity.CreateLocal(
            UserIdentityId.New(), UserId.New(), ActorType.Human, username, Now, User.SystemUserId));

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(), UserId.New(), ActorType.Human, "boundary", Now, User.SystemUserId);

        AssertRefused(() => identity.ChangeUsername(username));
    }

    [Theory]
    [MemberData(nameof(SpacedUsernames))]
    public async Task USR_C1_refuses_it_writes_nothing_and_never_looks_it_up(string username)
    {
        var administrator = await CallerAsync("user-administrator");
        var email = $"boundary-{Guid.NewGuid():N}@example.test";
        var audit = await ScalarAsync<long>(_database.ConnectionString, "SELECT coalesce(max(sequence), 0) FROM audit.audit_record");
        var notifications = await ScalarAsync<long>(_database.ConnectionString, "SELECT count(*) FROM notification");
        var lookups = new List<string>();

        var refusal = await CreateAsync(administrator, username, email, lookups);

        Assert.Equal(Refusal, refusal);
        Assert.Empty(lookups);

        Assert.Equal(0, await ScalarAsync<long>(_database.ConnectionString, $"SELECT count(*) FROM app_user WHERE email = '{email}'"));
        Assert.Equal(audit, await ScalarAsync<long>(_database.ConnectionString, "SELECT coalesce(max(sequence), 0) FROM audit.audit_record"));
        Assert.Equal(notifications, await ScalarAsync<long>(_database.ConnectionString, "SELECT count(*) FROM notification"));
    }

    [Theory]
    [MemberData(nameof(SpacedUsernames))]
    public async Task IDN_Q3_refuses_it_and_never_looks_it_up(string username)
    {
        var reader = await CallerAsync("access-reviewer");
        var lookups = new List<string>();

        var refusal = await Assert.ThrowsAsync<DomainException>(() => AvailableAsync(reader, username, lookups));

        Assert.Equal(Refusal, refusal.Message);
        Assert.Empty(lookups);
    }

    /// <summary>A fresh database of its own: PRV-C3 only creates the FIRST human.</summary>
    [Theory]
    [MemberData(nameof(SpacedUsernames))]
    public async Task The_PRV_C3_provisioner_refuses_it_and_commits_nothing(string username)
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            await new PlatformProvisioner(context).ProvisionAsync(Now, CancellationToken.None);
        }

        await using (var context = database.CreateContext())
        {
            var refusal = await Assert.ThrowsAsync<DomainException>(
                () => new BootstrapAdministratorProvisioner(
                        context, new SecurityPolicyResolver(context), new UserTokenService())
                    .ProvisionAsync(
                        new BootstrapAdministratorRequest("Ada", "Lovelace", "Ada Lovelace", "ada@example.test", username),
                        Now,
                        (_, _) => Task.CompletedTask,
                        CancellationToken.None));

            Assert.Equal(Refusal, refusal.Message);
        }

        Assert.Equal(0, await ScalarAsync<long>(database.ConnectionString, "SELECT count(*) FROM app_user WHERE actor_type = 'Human'"));
    }

    // ---------------------------------------------------------------- UW-5

    /// <summary>Refused for its whitespace — never reported as "already exists".</summary>
    [Fact]
    public async Task USR_C1_refuses_a_spaced_held_username_for_its_whitespace()
    {
        var administrator = await CallerAsync("user-administrator");
        var held = $"held-{Guid.NewGuid():N}"[..20];

        Assert.Null(await CreateAsync(administrator, held, $"held-{Guid.NewGuid():N}@example.test", []));

        Assert.Equal(Refusal, await CreateAsync(administrator, $" {held}", $"held-{Guid.NewGuid():N}@example.test", []));
        Assert.Equal(Refusal, await CreateAsync(administrator, $"{held} ", $"held-{Guid.NewGuid():N}@example.test", []));
    }

    /// <summary>Today a 500: the blank value reached the lookup's argument guard. Validated first, it is the domain's 400.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task USR_C1_refuses_a_blank_username_with_the_domain_sentence(string username)
    {
        var administrator = await CallerAsync("user-administrator");
        var lookups = new List<string>();

        Assert.Equal(Blank, await CreateAsync(administrator, username, $"blank-{Guid.NewGuid():N}@example.test", lookups));
        Assert.Empty(lookups);
    }

    [Fact]
    public async Task USR_C1_accepts_an_inner_space_and_looks_it_up()
    {
        var administrator = await CallerAsync("user-administrator");
        var username = $"inner space-{Guid.NewGuid():N}"[..24];
        var lookups = new List<string>();

        Assert.Null(await CreateAsync(administrator, username, $"inner-{Guid.NewGuid():N}@example.test", lookups));
        Assert.Equal([username], lookups);
    }

    // ================================================================ harness

    private static void AssertRefused(Action action)
        => Assert.Equal(Refusal, Assert.Throws<DomainException>(action).Message);

    private async Task<UserId> CallerAsync(string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, $"boundary-{role}", role)).UserId;

    /// <summary>The real stack, with ExistsWithUsernameAsync recorded on the way through.</summary>
    private ServiceProvider Provider(List<string> lookups)
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString);

        services.AddScoped<IUserIdentityRepository>(
            provider => new RecordingIdentityRepository(
                new UserIdentityRepository(provider.GetRequiredService<SKSMCorpDbContext>()), lookups));

        return services.BuildServiceProvider(validateScopes: true);
    }

    private async Task<UsernameAvailabilityResult> AvailableAsync(UserId caller, string username, List<string> lookups)
    {
        await using var provider = Provider(lookups);
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UsernameAvailabilityQuery, UsernameAvailabilityResult>(
                new UsernameAvailabilityQuery(username), CancellationToken.None);
    }

    /// <summary>USR-C1: null when accepted, else the refusal's sentence.</summary>
    private async Task<string?> CreateAsync(UserId caller, string username, string email, List<string> lookups)
    {
        await using var provider = Provider(lookups);
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        try
        {
            await scope.ServiceProvider
                .GetRequiredService<ICommandDispatcher>()
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    new CreateUserCommand("Boundary", "Check", "Boundary Check", email, username),
                    CancellationToken.None);

            return null;
        }
        catch (DomainException refusal)
        {
            return refusal.Message;
        }
        catch (BusinessRuleViolationException refusal)
        {
            return refusal.Message;
        }
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private sealed class RecordingIdentityRepository(IUserIdentityRepository inner, List<string> lookups)
        : IUserIdentityRepository
    {
        public Task<bool> ExistsWithUsernameAsync(string username, CancellationToken cancellationToken)
        {
            lock (lookups)
                lookups.Add(username);

            return inner.ExistsWithUsernameAsync(username, cancellationToken);
        }

        public Task AddAsync(UserIdentity identity, CancellationToken cancellationToken)
            => inner.AddAsync(identity, cancellationToken);

        public Task<UserIdentity?> FindLocalByUsernameAsync(string username, CancellationToken cancellationToken)
            => inner.FindLocalByUsernameAsync(username, cancellationToken);

        public Task<IReadOnlyList<UserIdentityId>> FindPasswordResetCandidatesAsync(
            string emailOrUsername, CancellationToken cancellationToken)
            => inner.FindPasswordResetCandidatesAsync(emailOrUsername, cancellationToken);

        public Task<IReadOnlyList<UserIdentity>> FindLocalByUserIdAsync(UserId userId, CancellationToken cancellationToken)
            => inner.FindLocalByUserIdAsync(userId, cancellationToken);

        public Task<UserIdentity?> FindAsync(UserIdentityId userIdentityId, CancellationToken cancellationToken)
            => inner.FindAsync(userIdentityId, cancellationToken);

        public Task<IReadOnlyList<UserIdentity>> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken)
            => inner.FindByUserIdAsync(userId, cancellationToken);
    }
}
