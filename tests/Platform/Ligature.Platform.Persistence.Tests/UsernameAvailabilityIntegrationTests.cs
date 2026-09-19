using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Queries.UsernameAvailability;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// IDN-Q3 CheckUsernameAvailable (docs/requirements.md, "IDN-Q3
/// CheckUsernameAvailable on Create user", UA-1 to UA-3), against PostgreSQL.
///
/// THE COMPATIBILITY SEAM. IDN-Q3 exists to tell an administrator, before
/// submitting, what USR-C1 will decide. UA-2 holds the two to the same answer
/// value by value — active, inactive, case variants, unused, and a value held
/// only by an external identity — so the shared repository check is a proven
/// seam, not a coincidence.
///
/// Its own database: identities are seeded directly, including an external
/// one, which nothing in the product can create yet.
/// </summary>
public sealed class UsernameAvailabilityIntegrationTests : IClassFixture<ActivationDatabase>
{
    private readonly ActivationDatabase _database;

    public UsernameAvailabilityIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ---------------------------------------------------------------- UA-1

    [Fact]
    public async Task Held_local_usernames_of_any_status_and_case_are_unavailable()
    {
        var reader = await CallerAsync("access-reviewer");
        var active = await SeedLocalAsync(inactive: false);
        var inactive = await SeedLocalAsync(inactive: true);

        Assert.False(await AvailableAsync(reader, active));
        Assert.False(await AvailableAsync(reader, inactive));
        Assert.False(await AvailableAsync(reader, active.ToUpperInvariant()));
        Assert.False(await AvailableAsync(reader, inactive.ToUpperInvariant()));
    }

    [Fact]
    public async Task An_unused_username_and_one_held_only_externally_are_available()
    {
        var reader = await CallerAsync("access-reviewer");
        var external = await SeedExternalAsync();

        Assert.True(await AvailableAsync(reader, $"unused-{Guid.NewGuid():N}"));
        Assert.True(await AvailableAsync(reader, external));
    }

    // ---------------------------------------------------------------- UA-2

    /// <summary>
    /// For each value: ask IDN-Q3 first, then let USR-C1 decide. Available
    /// exactly when USR-C1 accepts; unavailable exactly when it refuses with
    /// its username message.
    /// </summary>
    [Fact]
    public async Task IDN_Q3_answers_exactly_as_USR_C1_decides()
    {
        var administrator = await CallerAsync("user-administrator");
        var active = await SeedLocalAsync(inactive: false);
        var inactive = await SeedLocalAsync(inactive: true);
        var external = await SeedExternalAsync();

        var candidates = new[]
        {
            active,
            inactive,
            active.ToUpperInvariant(),
            inactive.ToUpperInvariant(),
            external,
            $"unused-{Guid.NewGuid():N}",

            // Checked exactly as sent (UN2): USR-C1 does not trim today, so a
            // held name with surrounding spaces is a DIFFERENT username to both.
            // The Known Gap "Local usernames accept surrounding whitespace"
            // will make both refuse it, together.
            $" {active} ",
        };

        foreach (var username in candidates)
        {
            var available = await AvailableAsync(administrator, username);
            var refusal = await CreateAsync(administrator, username);

            if (available)
            {
                Assert.True(refusal is null, $"IDN-Q3 said '{username}' was available, but USR-C1 refused: {refusal}");
            }
            else
            {
                Assert.Equal("A user identity with this username already exists.", refusal);
            }
        }
    }

    // ---------------------------------------------------------------- UA-3

    [Fact]
    public async Task Without_identity_read_the_read_is_refused()
    {
        var security = await CallerAsync("security-administrator");

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => AvailableAsync(security, "anyone"));

        Assert.Contains("permission", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_a_caller_the_read_is_refused()
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => scope.ServiceProvider
                .GetRequiredService<IQueryDispatcher>()
                .SendAsync<UsernameAvailabilityQuery, UsernameAvailabilityResult>(
                    new UsernameAvailabilityQuery("anyone"), CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_username_is_refused(string username)
    {
        var reader = await CallerAsync("access-reviewer");

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => AvailableAsync(reader, username));

        Assert.Equal("A username is required.", refusal.Message);
    }

    [Fact]
    public async Task The_read_is_not_audited()
    {
        var reader = await CallerAsync("access-reviewer");
        var before = await ScalarAsync<long>("SELECT coalesce(max(sequence), 0) FROM audit.audit_record");

        await AvailableAsync(reader, "anyone-at-all");

        Assert.Equal(before, await ScalarAsync<long>("SELECT coalesce(max(sequence), 0) FROM audit.audit_record"));
    }

    // ================================================================ harness

    private async Task<UserId> CallerAsync(string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, $"idn-q3-{role}", role)).UserId;

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private async Task<bool> AvailableAsync(UserId caller, string username)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        var result = await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UsernameAvailabilityQuery, UsernameAvailabilityResult>(
                new UsernameAvailabilityQuery(username), CancellationToken.None);

        return result.Available;
    }

    /// <summary>USR-C1 with this username: null when accepted, else the refusal.</summary>
    private async Task<string?> CreateAsync(UserId caller, string username)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        try
        {
            await scope.ServiceProvider
                .GetRequiredService<ICommandDispatcher>()
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    new CreateUserCommand("Idn", "Q3", "Idn Q3", $"idn-q3-{Guid.NewGuid():N}@example.test", username),
                    CancellationToken.None);

            return null;
        }
        catch (BusinessRuleViolationException refusal)
        {
            return refusal.Message;
        }
    }

    private async Task<string> SeedLocalAsync(bool inactive)
    {
        var userId = await SeedUserAsync();
        var id = Guid.NewGuid();
        var username = $"Held-{id:N}"[..20];
        var system = User.SystemUserId.Value;
        var status = inactive ? $"'Inactive', now() - interval '1 day', '{system}'" : "'Active', NULL, NULL";

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, deactivated_at, deactivated_by,
                                        created_at, created_by)
             VALUES ('{id}', '{userId}', 'Human', 'Local', 'Application', '{id}', '{username}',
                     {status}, now() - interval '30 days', '{system}')
             """);

        return username;
    }

    private async Task<string> SeedExternalAsync()
    {
        var userId = await SeedUserAsync();
        var id = Guid.NewGuid();
        var username = $"external-{id:N}"[..24];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{id}', '{userId}', 'Human', 'External', 'EntraId', 'subject-{id:N}', '{username}', 'Active',
                     now() - interval '30 days', '{system}')
             """);

        return username;
    }

    private async Task<Guid> SeedUserAsync()
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Holds', 'Username', 'Holds Username', 'holder-{id:N}@example.test',
                     'Active', now() - interval '1 year', '{system}', now(), '{system}')
             """);

        return id;
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
