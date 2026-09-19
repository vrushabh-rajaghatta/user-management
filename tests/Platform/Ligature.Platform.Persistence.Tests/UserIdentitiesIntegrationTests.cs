using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Queries.UserIdentities;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// IDN-Q1 GetUserIdentities, as amended (docs/requirements.md, "IDN-Q1
/// GetUserIdentities and Unlock on the User detail page", ID-1 and ID-2),
/// against PostgreSQL.
///
/// THE LOCK STATE IS A READ-TIME PROJECTION of the credential's LockedUntil
/// against the server's clock, by CRD-C6's rule — a lock currently in force.
/// The four materially different cases are each seeded: in force, expired,
/// failures without a lock, and no credential at all.
///
/// Its own database: identities are seeded directly, including an external one,
/// which nothing in the product can create yet.
/// </summary>
public sealed class UserIdentitiesIntegrationTests : IClassFixture<ActivationDatabase>
{
    private readonly ActivationDatabase _database;

    public UserIdentitiesIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ---------------------------------------------------------------- ID-1

    /// <summary>
    /// Every identity of the user — local and external, active and inactive —
    /// oldest first, with exactly the amended fields.
    /// </summary>
    [Fact]
    public async Task Every_identity_is_read_oldest_first_with_the_amended_fields()
    {
        var reader = await CallerAsync("access-reviewer");
        var user = await SeedUserAsync();

        var local = await AddLocalAsync(user, daysAgo: 30, credential: CredentialKind.None);
        var externalInactive = await AddExternalAsync(user, daysAgo: 20, inactive: true);
        var externalActive = await AddExternalAsync(user, daysAgo: 10, inactive: false);

        var result = await ReadAsync(reader, user);

        Assert.Equal(
            [local.Id, externalInactive.Id, externalActive.Id],
            result.Identities.Select(x => x.UserIdentityId.Value));

        var first = result.Identities[0];
        Assert.Equal(IdentityType.Local, first.Type);
        Assert.Equal("Application", first.Provider);
        Assert.Equal(local.Username, first.Username);
        Assert.Equal(UserStatus.Active, first.Status);
        Assert.Null(first.DeactivatedAt);

        var second = result.Identities[1];
        Assert.Equal(IdentityType.External, second.Type);
        Assert.Equal("EntraId", second.Provider);
        Assert.Null(second.Username);
        Assert.Equal(UserStatus.Inactive, second.Status);
        Assert.NotNull(second.DeactivatedAt);

        Assert.All(result.Identities, x => Assert.False(x.Locked));
        Assert.All(result.Identities, x => Assert.Null(x.LockedUntil));
    }

    /// <summary>Ties on creation break by identity id, so the order is deterministic.</summary>
    [Fact]
    public async Task Identities_created_at_the_same_instant_are_ordered_by_id()
    {
        var reader = await CallerAsync("access-reviewer");
        var user = await SeedUserAsync();

        var a = await AddExternalAsync(user, daysAgo: 5, inactive: false, fixedCreatedAt: true);
        var b = await AddExternalAsync(user, daysAgo: 5, inactive: false, fixedCreatedAt: true);

        var result = await ReadAsync(reader, user);

        Assert.Equal(
            new[] { a.Id, b.Id }.Order(),
            result.Identities.Select(x => x.UserIdentityId.Value));
    }

    [Fact]
    public async Task An_unknown_user_and_the_System_actor_are_refused_as_unknown()
    {
        var reader = await CallerAsync("access-reviewer");

        await AssertRefusedAsync(() => ReadAsync(reader, new UserId(Guid.NewGuid())), "The user does not exist.");
        await AssertRefusedAsync(() => ReadAsync(reader, User.SystemUserId), "The user does not exist.");
    }

    /// <summary>identity.read is the read's permission: the security administrator does not hold it.</summary>
    [Fact]
    public async Task A_caller_without_identity_read_is_refused()
    {
        var securityAdministrator = await CallerAsync("security-administrator");
        var user = await SeedUserAsync();
        await AddLocalAsync(user, daysAgo: 1, credential: CredentialKind.None);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => ReadAsync(securityAdministrator, user));
    }

    /// <summary>Reads are not events: nothing is written to the trail.</summary>
    [Fact]
    public async Task Reading_identities_records_nothing()
    {
        var reader = await CallerAsync("access-reviewer");
        var user = await SeedUserAsync();
        await AddLocalAsync(user, daysAgo: 1, credential: CredentialKind.LockedInForce);

        var before = await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record");

        await ReadAsync(reader, user);

        Assert.Equal(before, await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record"));
    }

    // ---------------------------------------------------------------- ID-2

    [Fact]
    public async Task A_lock_in_force_reads_as_locked_until_its_instant()
    {
        var reader = await CallerAsync("access-reviewer");
        var user = await SeedUserAsync();
        var identity = await AddLocalAsync(user, daysAgo: 1, credential: CredentialKind.LockedInForce);

        var view = Assert.Single((await ReadAsync(reader, user)).Identities);

        Assert.True(view.Locked);
        Assert.Equal(identity.LockedUntil, view.LockedUntil);
    }

    [Theory]
    [InlineData(CredentialKind.LockExpired)]
    [InlineData(CredentialKind.FailuresWithoutLock)]
    [InlineData(CredentialKind.None)]
    public async Task Anything_but_a_lock_in_force_reads_as_not_locked(CredentialKind kind)
    {
        var reader = await CallerAsync("access-reviewer");
        var user = await SeedUserAsync();
        await AddLocalAsync(user, daysAgo: 1, credential: kind);

        var view = Assert.Single((await ReadAsync(reader, user)).Identities);

        Assert.False(view.Locked);
        Assert.Null(view.LockedUntil);
    }

    // ================================================================ harness

    public enum CredentialKind
    {
        None,
        LockedInForce,
        LockExpired,
        FailuresWithoutLock,
    }

    private sealed record Seeded(Guid Id, string? Username, DateTimeOffset? LockedUntil);

    private async Task<UserId> CallerAsync(string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, $"idn-q1-{role}", role)).UserId;

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private async Task<UserIdentitiesResult> ReadAsync(UserId caller, UserId target)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UserIdentitiesQuery, UserIdentitiesResult>(new UserIdentitiesQuery(target), CancellationToken.None);
    }

    private static async Task AssertRefusedAsync(Func<Task> dispatch, string message)
    {
        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(dispatch);
        Assert.Equal(message, refusal.Message);
    }

    private async Task<UserId> SeedUserAsync()
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Many', 'Identities', 'Many Identities', 'identities-{id:N}@example.test',
                     'Active', now() - interval '1 year', '{system}', now(), '{system}')
             """);

        return new UserId(id);
    }

    private async Task<Seeded> AddLocalAsync(UserId user, int daysAgo, CredentialKind credential)
    {
        var id = Guid.NewGuid();
        var username = $"idn-{id:N}"[..24];
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{id}', '{user.Value}', 'Human', 'Local', 'Application', '{id}', '{username}', 'Active',
                     now() - interval '{daysAgo} days', '{system}')
             """);

        DateTimeOffset? lockedUntil = null;

        if (credential != CredentialKind.None)
        {
            var (failures, until) = credential switch
            {
                CredentialKind.LockedInForce => (5, "date_trunc('second', now()) + interval '1 hour'"),
                CredentialKind.LockExpired => (5, "now() - interval '1 minute'"),
                CredentialKind.FailuresWithoutLock => (3, "NULL"),
                _ => throw new ArgumentOutOfRangeException(nameof(credential)),
            };

            var hashed = new Ligature.Platform.Persistence.Services.PasswordHasher().Hash("an-identity-test-password");

            await ExecuteAsync(
                $"""
                 INSERT INTO credential (id, user_identity_id, identity_type, password_hash, password_algorithm,
                                         password_changed_at, must_change_password, failed_attempt_count,
                                         locked_until, created_at, created_by)
                 VALUES ('{Guid.NewGuid()}', '{id}', 'Local', '{hashed.Hash}', '{hashed.Algorithm}',
                         now() - interval '1 day', false, {failures}, {until}, now() - interval '1 day', '{system}')
                 """);

            if (credential == CredentialKind.LockedInForce)
            {
                var raw = await ScalarAsync<DateTime>(
                    $"SELECT locked_until FROM credential WHERE user_identity_id = '{id}'");

                lockedUntil = new DateTimeOffset(DateTime.SpecifyKind(raw, DateTimeKind.Utc));
            }
        }

        return new Seeded(id, username, lockedUntil);
    }

    private async Task<Seeded> AddExternalAsync(UserId user, int daysAgo, bool inactive, bool fixedCreatedAt = false)
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;
        var created = fixedCreatedAt ? "'2026-09-01T00:00:00Z'::timestamptz" : $"now() - interval '{daysAgo} days'";

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by,
                                        deactivated_at, deactivated_by)
             VALUES ('{id}', '{user.Value}', 'Human', 'External', 'EntraId', 'external-{id:N}', NULL,
                     '{(inactive ? "Inactive" : "Active")}', {created}, '{system}',
                     {(inactive ? "now() - interval '1 day'" : "NULL")}, {(inactive ? $"'{system}'" : "NULL")})
             """);

        return new Seeded(id, null, null);
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
