using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Users.Commands.ChangePassword;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Tests.Users;

/// <summary>
/// CRD-C4, D3 — the half of "a locked credential is refused" that the
/// database cannot show: that the refusal still pays a real derivation.
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

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => Handle(hasher, locked: true));

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

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => Handle(hasher, locked: false));

        Assert.Equal(1, hasher.VerifyCalls);
        Assert.Equal(0, hasher.DecoyCalls);
    }

    private static Task<ChangePasswordResult> Handle(SpyPasswordHasher hasher, bool locked)
    {
        var userId = UserId.New();

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
            new OneSessionRepository(session),
            new OneIdentityRepository(identity),
            new OneCredentialRepository(credential),
            new EmptyHistoryRepository(),
            new BaselinePolicyResolver(),
            hasher,
            OpenCommand());

        return handler.Handle(
            new ChangePasswordCommand(session.Id, "whatever-was-typed", "a-new-password-long-enough"),
            CancellationToken.None);
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

    private sealed class OneSessionRepository(UserSession session) : IUserSessionRepository
    {
        public Task AddAsync(UserSession added, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<UserSession?> FindAsync(
            UserSessionId sessionId, CancellationToken cancellationToken)
            => Task.FromResult<UserSession?>(sessionId == session.Id ? session : null);

        public Task<UserSession?> FindActiveAsync(
            UserSessionId sessionId, DateTimeOffset now, TimeSpan idleTimeout,
            CancellationToken cancellationToken)
            => Task.FromResult<UserSession?>(null);

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
