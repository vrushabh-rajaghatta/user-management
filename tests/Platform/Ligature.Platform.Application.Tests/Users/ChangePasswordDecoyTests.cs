using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Users.Commands.ChangePassword;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Users;

/// <summary>
/// CRD-C4, D3 — the half of "a locked credential is refused" that the
/// database cannot show: that the refusal still pays a real derivation.
///
/// Since the attempt limit (docs/requirements.md, "CRD-C4 — limiting
/// current-password attempts per session"), a wrong or locked attempt is a
/// COUNTED refusal: it returns Refused, so the session's counter commits,
/// instead of throwing. L8 keeps the two alike — the same cost, the same count,
/// the same outcome.
///
/// A locked refusal that skipped the hasher would return measurably faster
/// than a wrong password, and that difference would tell a session holder the
/// account is locked. The integration tests prove the refusal and that
/// nothing commits; only a spy on the hasher can prove the cost was paid, and
/// that the stored hash was NOT consulted while locked.
/// </summary>
public sealed class ChangePasswordDecoyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_locked_credential_pays_a_decoy_derivation_and_never_verifies()
    {
        var hasher = new SpyPasswordHasher();

        var result = await Handle(hasher, locked: true);

        Assert.Equal(ChangePasswordOutcome.Refused, result.Outcome);

        Assert.True(
            hasher.DecoyCalls == 1,
            "A locked credential must still pay the derivation cost, or how quickly "
            + "the refusal returns reveals that the account is locked.");

        Assert.Equal(0, hasher.VerifyCalls);
    }

    /// <summary>
    /// The contrast that makes the first test mean something: an unlocked
    /// credential is verified for real, and no decoy is spent.
    /// </summary>
    [Fact]
    public async Task An_unlocked_credential_is_verified_against_the_stored_hash()
    {
        var hasher = new SpyPasswordHasher();

        var result = await Handle(hasher, locked: false);

        Assert.Equal(ChangePasswordOutcome.Refused, result.Outcome);
        Assert.Equal(1, hasher.VerifyCalls);
        Assert.Equal(0, hasher.DecoyCalls);
    }

    /// <summary>
    /// L8, side-channel consistency: the two refusals mutate the session the
    /// same way. If only a wrong password wrote the counter, the write would
    /// be one more way to tell "locked" from "wrong".
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_wrong_or_locked_attempt_counts_once_against_this_session(bool locked)
    {
        var (result, session, _) = await HandleWithState(new SpyPasswordHasher(), locked);

        Assert.Equal(ChangePasswordOutcome.Refused, result.Outcome);
        Assert.Equal(1, session.FailedPasswordChangeAttempts);
    }

    /// <summary>
    /// The serialisation point comes FIRST: the user's row lock is taken
    /// before the session is read, so the decision is made on what the lock
    /// protects. (That the database really holds it is the integration tests'
    /// to prove.)
    /// </summary>
    [Fact]
    public async Task The_users_row_lock_is_taken_before_the_session_is_read()
    {
        var (_, _, trace) = await HandleWithState(new SpyPasswordHasher(), locked: false);

        Assert.Equal("lock", trace[0]);
        Assert.Contains("session", trace);
    }

    private static async Task<ChangePasswordResult> Handle(SpyPasswordHasher hasher, bool locked)
        => (await HandleWithState(hasher, locked)).Result;

    private static async Task<(ChangePasswordResult Result, UserSession Session, List<string> Trace)> HandleWithState(
        SpyPasswordHasher hasher, bool locked)
    {
        var trace = new List<string>();

        var user = User.CreateHuman(
            UserId.New(), "Decoy", "Person", "Decoy Person", "decoy.person@example.test",
            Now.AddDays(-2), User.SystemUserId);

        var userId = user.Id;

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(), userId, ActorType.Human, "decoy.person", Now, User.SystemUserId);

        var credential = Credential.Create(
            CredentialId.New(), identity.Id, IdentityType.Local, "$stored", "alg",
            Now.AddDays(-1), mustChangePassword: false, Now.AddDays(-1), User.SystemUserId);

        if (locked)
            credential.Lock(Now.AddHours(1));

        var session = UserSession.Create(
            UserSessionId.New(), identity.Id, Now.AddMinutes(-5), Now.AddHours(8),
            ipAddress: null, userAgent: null);

        var handler = new ChangePasswordCommandHandler(
            new FixedClock(),
            new CallerContext(userId),
            new PassThroughUnitOfWork(),
            new LockingUserRepository(user, trace),
            new OneSessionRepository(session, trace),
            new OneIdentityRepository(identity),
            new OneCredentialRepository(credential),
            new EmptyHistoryRepository(),
            new BaselinePolicyResolver(),
            hasher,
            OpenCommand());

        var result = await handler.Handle(
            new ChangePasswordCommand(session.Id, "whatever-was-typed", "a-new-password-long-enough"),
            CancellationToken.None);

        return (result, session, trace);
    }

    private static IAuditEvents OpenCommand()
    {
        var events = new ScopedAuditEvents();
        ((IAuditEmissionScope)events).BeginCommand(Guid.NewGuid(), DateTimeOffset.UtcNow);
        return events;
    }

    private sealed class SpyPasswordHasher : IPasswordHasher
    {
        public int DecoyCalls { get; private set; }

        public int VerifyCalls { get; private set; }

        public PasswordHashMaterial Hash(string password) => new("$h", "alg");

        public PasswordVerificationResult Verify(
            string password, string storedHash, string storedAlgorithm)
        {
            VerifyCalls++;
            return new PasswordVerificationResult(false, false);
        }

        public void VerifyDecoy(string password) => DecoyCalls++;
    }

    private sealed class CallerContext(UserId userId) : IExecutionContext
    {
        public UserId UserId { get; } = userId;

        public ActorType ActorType => ActorType.Human;

        public bool IsAuthenticated => true;

        public ActorIdentity Identity => TestActorIdentity.Human();

        public AuthorizingAssignment? Authority => null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
            => operation(cancellationToken);
    }

    /// <summary>Records when the user's row lock is asked for.</summary>
    private sealed class LockingUserRepository(User user, List<string> trace) : IUserRepository
    {
        public Task<User?> FindForUpdateAsync(UserId userId, CancellationToken cancellationToken)
        {
            trace.Add("lock");
            return Task.FromResult<User?>(userId == user.Id ? user : null);
        }

        public Task<bool> ExistsActiveHumanWithEmailAsync(
            EmailAddress email, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(User added, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<User?> FindAsync(UserId userId, CancellationToken cancellationToken)
            => Task.FromResult<User?>(userId == user.Id ? user : null);
    }

    private sealed class OneSessionRepository(UserSession session, List<string> trace) : IUserSessionRepository
    {
        public Task AddAsync(UserSession added, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<UserSession?> FindAsync(
            UserSessionId sessionId, CancellationToken cancellationToken)
        {
            trace.Add("session");
            return Task.FromResult<UserSession?>(sessionId == session.Id ? session : null);
        }

        public Task<UserSession?> FindActiveAsync(
            UserSessionId sessionId, DateTimeOffset now, TimeSpan idleTimeout,
            CancellationToken cancellationToken)
        {
            trace.Add("session");
            return Task.FromResult<UserSession?>(
                sessionId == session.Id && session.IsActive(now, idleTimeout) ? session : null);
        }

        public Task<IReadOnlyList<UserSession>> FindActiveForUserAsync(
            UserId userId, DateTimeOffset now, TimeSpan idleTimeout,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserSession>>([]);

        public Task<IReadOnlyList<UserSession>> FindOtherActiveForIdentityAsync(
            UserIdentityId identityId, UserSessionId excluding, DateTimeOffset now,
            TimeSpan idleTimeout, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserSession>>([]);

        public Task RecordActivityAsync(
            UserSessionId sessionId, DateTimeOffset now, TimeSpan staleness,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class OneIdentityRepository(UserIdentity identity) : IUserIdentityRepository
    {
        public Task<IReadOnlyList<UserIdentity>> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsWithUsernameAsync(string username, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(UserIdentity added, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<UserIdentity?> FindLocalByUsernameAsync(string username, CancellationToken cancellationToken)
            => Task.FromResult<UserIdentity?>(null);

        public Task<IReadOnlyList<UserIdentityId>> FindPasswordResetCandidatesAsync(
            string emailOrUsername, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserIdentityId>>([]);

        public Task<IReadOnlyList<UserIdentity>> FindLocalByUserIdAsync(
            UserId userId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserIdentity>>([identity]);

        public Task<UserIdentity?> FindAsync(
            UserIdentityId userIdentityId, CancellationToken cancellationToken)
            => Task.FromResult<UserIdentity?>(userIdentityId == identity.Id ? identity : null);
    }

    private sealed class OneCredentialRepository(Credential credential) : ICredentialRepository
    {
        public Task AddAsync(Credential added, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Credential?> FindByIdentityAsync(
            UserIdentityId userIdentityId, CancellationToken cancellationToken)
            => Task.FromResult<Credential?>(
                userIdentityId == credential.UserIdentityId ? credential : null);
    }

    private sealed class EmptyHistoryRepository : IPasswordHistoryRepository
    {
        public Task AddAsync(PasswordHistory history, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<PasswordHistory>> FindRecentAsync(
            UserIdentityId userIdentityId, int depth, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PasswordHistory>>([]);
    }

    private sealed class BaselinePolicyResolver : ISecurityPolicyResolver
    {
        public Task<SecurityPolicySettings> GetEffectiveSettingsAsync(
            DateTimeOffset at, CancellationToken cancellationToken)
            => Task.FromResult(SecurityBaseline.Current);
    }
}
