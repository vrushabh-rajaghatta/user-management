using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.SignOut;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.SharedKernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// SES-C2 end to end. D6's single termination mechanism: signing out writes the
/// same three revocation columns an administrator or a cascade would, with a
/// different actor and reason.
///
/// The property most of these tests exist to protect is that a caller learns
/// NOTHING about a session id they do not own. Session ids are supplied by the
/// caller, so any observable difference between "not yours", "does not exist"
/// and "already revoked" would let one be probed for the others.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class SignOutIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Signing_out_revokes_the_session_as_a_self_revocation()
    {
        await RunAsync(async (provider, owner, _) =>
        {
            await SignOutAsync(provider, owner.UserId, owner.SessionId);

            var session = await ReadSessionAsync(owner.SessionId);

            Assert.NotNull(session.RevokedAt);

            // D6 — the user revokes their own session; the columns are the same
            // ones an administrator or a cascade writes.
            Assert.Equal(owner.UserId.Value, session.RevokedBy);
            Assert.Equal("Logout", session.RevocationReason);
        });
    }

    /// <summary>
    /// "Double sign-out must be idempotent, not an error." And the first
    /// revocation's actor, reason and instant are write-once — a second call
    /// must not overwrite the record of who ended the session and when.
    /// </summary>
    [Fact]
    public async Task A_second_sign_out_is_a_no_op_that_preserves_the_first()
    {
        await RunAsync(async (provider, owner, _) =>
        {
            await SignOutAsync(provider, owner.UserId, owner.SessionId);

            var first = await ReadSessionAsync(owner.SessionId);

            await SignOutAsync(provider, owner.UserId, owner.SessionId);

            var second = await ReadSessionAsync(owner.SessionId);

            Assert.Equal(first.RevokedAt, second.RevokedAt);
            Assert.Equal(first.RevokedBy, second.RevokedBy);
            Assert.Equal(first.RevocationReason, second.RevocationReason);
        });
    }

    /// <summary>
    /// THE security test. Without the ownership check SES-C2 would be SES-C3
    /// RevokeSession — which requires the session.revoke permission — available
    /// to every authenticated user against any session id they can guess.
    /// </summary>
    [Fact]
    public async Task A_caller_cannot_sign_out_someone_elses_session()
    {
        await RunAsync(async (provider, owner, stranger) =>
        {
            await SignOutAsync(provider, stranger.UserId, owner.SessionId);

            var session = await ReadSessionAsync(owner.SessionId);

            Assert.Null(session.RevokedAt);
            Assert.Null(session.RevokedBy);
            Assert.Null(session.RevocationReason);
        });
    }

    /// <summary>
    /// All three collapse to one observable outcome: the command completes and
    /// says nothing. An unknown id must not be distinguishable from a foreign
    /// one, or ids become probeable.
    /// </summary>
    [Fact]
    public async Task Unknown_foreign_and_already_revoked_are_indistinguishable()
    {
        await RunAsync(async (provider, owner, stranger) =>
        {
            // Unknown id.
            var unknown = await SignOutAsync(
                provider, owner.UserId, UserSessionId.New());

            // Someone else's session.
            var foreign = await SignOutAsync(
                provider, stranger.UserId, owner.SessionId);

            // Their own, twice.
            await SignOutAsync(provider, owner.UserId, owner.SessionId);
            var repeated = await SignOutAsync(
                provider, owner.UserId, owner.SessionId);

            // Same type, and it carries no state at all to differ by.
            Assert.Equal(unknown, foreign);
            Assert.Equal(foreign, repeated);

            Assert.Empty(
                typeof(SignOutResult)
                    .GetProperties()
                    .Where(x => x.Name != "EqualityContract"));
        });
    }

    /// <summary>
    /// Expiry is derived from timestamps rather than stored, so there is no
    /// state for revocation to conflict with — an expired session is still
    /// revocable, and D6's single mechanism stays single.
    /// </summary>
    [Fact]
    public async Task An_expired_session_is_still_revocable()
    {
        await RunAsync(async (provider, owner, _) =>
        {
            await ExpireAsync(owner.SessionId);

            await SignOutAsync(provider, owner.UserId, owner.SessionId);

            Assert.NotNull((await ReadSessionAsync(owner.SessionId)).RevokedAt);
        });
    }

    /// <summary>
    /// Not anonymous. SES-C2 declares no catalogue permission because its
    /// permission is "self" — the case that would have become anonymous had the
    /// marker been inferred from the absence of IAuthorizableCommand.
    /// </summary>
    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        await RunAsync(async (provider, owner, _) =>
        {
            using var scope = provider.CreateScope();

            // No Establish call.
            await Assert.ThrowsAsync<AuthenticationFailedException>(
                () => scope.ServiceProvider
                    .GetRequiredService<ICommandDispatcher>()
                    .SendAsync<SignOutCommand, SignOutResult>(
                        new SignOutCommand(owner.SessionId),
                        CancellationToken.None));

            Assert.Null((await ReadSessionAsync(owner.SessionId)).RevokedAt);
        });
    }

    /// <summary>
    /// Inv. 26 — sessions exist for interactive human authentication, so a
    /// machine actor has none to end. The command declares that with
    /// IHumanActorOnlyCommand rather than relying on the ownership check to
    /// refuse incidentally, which gives a fail-loud boundary if the actor model
    /// ever expands.
    ///
    /// Unreachable through normal means today — AU11 blocks agent creation —
    /// but the execution context can carry an agent, and the pipeline must
    /// refuse before the handler runs.
    /// </summary>
    [Fact]
    public async Task A_non_human_caller_is_refused_by_the_pipeline()
    {
        await RunAsync(async (provider, owner, _) =>
        {
            using var scope = provider.CreateScope();

            scope.ServiceProvider
                .GetRequiredService<IExecutionContextInitializer>()
                .Establish(owner.UserId, ActorType.Agent, TestActorIdentity.NonHuman());

            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => scope.ServiceProvider
                    .GetRequiredService<ICommandDispatcher>()
                    .SendAsync<SignOutCommand, SignOutResult>(
                        new SignOutCommand(owner.SessionId),
                        CancellationToken.None));

            Assert.Null((await ReadSessionAsync(owner.SessionId)).RevokedAt);
        });
    }

    // ------------------------------------------------------------- harness

    private sealed record Actor(
        UserId UserId, UserIdentityId IdentityId, UserSessionId SessionId);

    private static async Task<SignOutResult> SignOutAsync(
        IServiceProvider provider, UserId caller, UserSessionId sessionId)
    {
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<SignOutCommand, SignOutResult>(
                new SignOutCommand(sessionId), CancellationToken.None);
    }

    private static async Task RunAsync(
        Func<IServiceProvider, Actor, Actor, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        // Two permanent callers, distinguished by purpose rather than by role:
        // signing out is now audited, so an owner who signs out is the actor of
        // a record and their rows can never be deleted. The SESSIONS are fresh
        // every run, which is what each test actually needs; the people are not.
        var owner = await SeedActorAsync("signout-owner");
        var stranger = await SeedActorAsync("signout-stranger");

        try
        {
            await using var provider = BuildProvider();

            await body(provider, owner, stranger);
        }
        finally
        {
            await DeleteSessionAsync(owner.SessionId);
            await DeleteSessionAsync(stranger.SessionId);
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(TestDatabase.ConnectionString);

        services.AddSingleton<IClock>(new FixedClock(Now));

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// The permanent caller for this purpose, plus a session of this run's own.
    /// </summary>
    private static async Task<Actor> SeedActorAsync(string label)
    {
        var caller = await PermanentTestCaller.EnsureAsync(
            TestDatabase.ConnectionString, label, roleCode: null);

        var session = UserSession.Create(
            UserSessionId.New(), caller.IdentityId, Now,
            Now + SecurityBaseline.Current.SessionAbsoluteTimeout,
            ipAddress: null, userAgent: "signout-tests/1.0");

        await using var context = CreateContext();
        context.Add(session);
        await context.SaveChangesAsync(CancellationToken.None);

        return new Actor(caller.UserId, caller.IdentityId, session.Id);
    }

    private static async Task DeleteSessionAsync(UserSessionId sessionId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            "DELETE FROM user_session WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", sessionId.Value);

        await command.ExecuteNonQueryAsync();
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new FixedClock(Now), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    private sealed record SessionRow(
        DateTimeOffset? RevokedAt, Guid? RevokedBy, string? RevocationReason);

    private static async Task<SessionRow> ReadSessionAsync(UserSessionId id)
    {
        await using var connection = await TestDatabase.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT revoked_at, revoked_by, revocation_reason
            FROM user_session WHERE id = @id
            """, connection);

        command.Parameters.AddWithValue("id", id.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "The session row is missing.");

        return new SessionRow(
            reader.IsDBNull(0) ? null : Offset(reader.GetValue(0)),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static DateTimeOffset? Offset(object? value)
        => value switch
        {
            null or DBNull => null,
            DateTimeOffset offset => offset,
            DateTime utc => new DateTimeOffset(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Not a timestamp."),
        };

    /// <summary>
    /// ck_user_session_expires_at requires expiry after creation, so an expired
    /// session is one created earlier still — not one that never made sense.
    /// </summary>
    private static async Task ExpireAsync(UserSessionId id)
    {
        await using var connection = await TestDatabase.OpenAsync();

        // Re-created rather than backdated: G4 makes created_at and expires_at
        // immutable, so a session's age is fixed at insert. The DELETE's
        // RETURNING feeds the INSERT, so the row keeps its id and every other
        // column, and the caller's carrier still names this session.
        await using var command = new NpgsqlCommand(
            """
            WITH removed AS (
                DELETE FROM user_session WHERE id = @id RETURNING *
            )
            INSERT INTO user_session
                (id, user_identity_id, created_at, last_activity_at, expires_at,
                 revoked_at, revoked_by, revocation_reason, ip_address, user_agent)
            SELECT id, user_identity_id, @created, @created, @expires,
                   revoked_at, revoked_by, revocation_reason, ip_address, user_agent
            FROM removed
            """, connection);

        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("created", Now.AddDays(-2));
        command.Parameters.AddWithValue("expires", Now.AddDays(-1));

        await command.ExecuteNonQueryAsync();
    }


    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
