using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Users;

/// <summary>
/// SES-C1's own half of the invariant, held by the handler whatever reaches
/// it: a sign-in attempt that cannot establish the authenticated identity must
/// not mutate the target credential as though authentication succeeded.
///
/// The pipeline refuses such an attempt before the handler runs. This proves
/// the handler would not have done the damage anyway — the password verifies,
/// the stored algorithm is stale, the counter is non-zero, and the establisher
/// declines. Nothing about the credential may change, no rehash may be
/// computed, and no session may be created.
/// </summary>
public sealed class SignInCredentialMutationTests
{
    private const string Username = "guarded.person";

    private const string StoredHash = "$stored-hash";

    private const string StoredAlgorithm = "stale-algorithm";

    private static readonly DateTimeOffset Now =
        new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_attempt_that_cannot_establish_the_verified_identity_leaves_the_credential_untouched()
    {
        var user = User.CreateHuman(
            UserId.New(), "Guarded", "Person", "Guarded Person",
            "guarded.person@example.test", Now.AddDays(-30), User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(), user.Id, ActorType.Human, Username,
            Now.AddDays(-30), User.SystemUserId);

        var credential = Credential.Create(
            CredentialId.New(), identity.Id, IdentityType.Local,
            StoredHash, StoredAlgorithm,
            passwordChangedAt: Now.AddDays(-30), mustChangePassword: false,
            createdAt: Now.AddDays(-30), createdBy: User.SystemUserId);

        credential.RecordFailedAttempt();
        credential.RecordFailedAttempt();

        var hasher = new VerifyingHasher();
        var sessions = new RecordingSessionRepository();

        var result = await new SignInCommandHandler(
                new FixedClock(),
                new PassThroughUnitOfWork(),
                new SingleIdentityRepository(identity),
                new SingleUserRepository(user),
                new SingleCredentialRepository(credential),
                sessions,
                new BaselinePolicyResolver(),
                hasher,
                new DecliningEstablisher(),
                OpenCommand())
            .Handle(new SignInCommand(Username, "the-right-password", null, null), CancellationToken.None);

        Assert.False(result.Succeeded);

        Assert.Equal(2, credential.FailedAttemptCount);
        Assert.Null(credential.LockedUntil);
        Assert.Equal(StoredHash, credential.PasswordHash);
        Assert.Equal(StoredAlgorithm, credential.PasswordAlgorithm);

        Assert.Equal(0, hasher.HashCalls);
        Assert.Empty(sessions.Added);
    }

    private static IAuditEvents OpenCommand()
    {
        var events = new ScopedAuditEvents();

        ((IAuditEmissionScope)events).BeginCommand(Guid.NewGuid(), Now);

        return events;
    }

    /// <summary>
    /// Every verification succeeds and asks for a rehash, so a handler that
    /// rehashed before establishing would be caught computing one.
    /// </summary>
    private sealed class VerifyingHasher : IPasswordHasher
    {
        public int HashCalls { get; private set; }

        public PasswordHashMaterial Hash(string password)
        {
            HashCalls++;

            return new PasswordHashMaterial("$rehashed", "current-algorithm");
        }

        public PasswordVerificationResult Verify(
            string password, string storedHash, string storedAlgorithm)
            => new(IsValid: true, NeedsRehash: true);

        public void VerifyDecoy(string password)
        {
        }
    }

    /// <summary>
    /// The identity whose password verified is not the one the scope may act
    /// as — what BearerActorEstablisher reports for a different, already
    /// established caller.
    /// </summary>
    private sealed class DecliningEstablisher : IBearerActorEstablisher
    {
        public Task<bool> EstablishAsync(
            UserIdentityId identityId, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
            => operation(cancellationToken);
    }

    private sealed class SingleIdentityRepository(UserIdentity identity) : IUserIdentityRepository
    {
        public Task<IReadOnlyList<UserIdentity>> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsWithUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult(username == identity.Username);

        public Task AddAsync(UserIdentity added, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<UserIdentity?> FindLocalByUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult(username == identity.Username ? identity : null);

        public Task<IReadOnlyList<UserIdentityId>> FindPasswordResetCandidatesAsync(
            string emailOrUsername, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserIdentityId>>([]);

        public Task<IReadOnlyList<UserIdentity>> FindLocalByUserIdAsync(
            UserId userId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserIdentity>>([identity]);

        public Task<UserIdentity?> FindAsync(
            UserIdentityId userIdentityId, CancellationToken cancellationToken)
            => Task.FromResult(userIdentityId == identity.Id ? identity : null);
    }

    private sealed class SingleUserRepository(User user) : IUserRepository
    {
        public Task<User?> FindForUpdateAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsActiveHumanWithEmailAsync(
            EmailAddress email, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(User added, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<User?> FindAsync(UserId userId, CancellationToken cancellationToken)
            => Task.FromResult(userId == user.Id ? user : null);
    }

    private sealed class SingleCredentialRepository(Credential credential) : ICredentialRepository
    {
        public Task AddAsync(Credential added, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Credential?> FindByIdentityAsync(
            UserIdentityId userIdentityId, CancellationToken cancellationToken)
            => Task.FromResult(
                userIdentityId == credential.UserIdentityId ? credential : null);
    }

    private sealed class RecordingSessionRepository : IUserSessionRepository
    {
        public List<UserSession> Added { get; } = [];

        public Task AddAsync(UserSession session, CancellationToken cancellationToken)
        {
            Added.Add(session);

            return Task.CompletedTask;
        }

        public Task<UserSession?> FindAsync(
            UserSessionId sessionId, CancellationToken cancellationToken)
            => Task.FromResult<UserSession?>(null);

        public Task<UserSession?> FindActiveAsync(
            UserSessionId sessionId, DateTimeOffset now, TimeSpan idleTimeout,
            CancellationToken cancellationToken)
            => Task.FromResult<UserSession?>(null);

        public Task<IReadOnlyList<UserSession>> FindActiveForUserAsync(
            UserId userId, DateTimeOffset now, TimeSpan idleTimeout,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserSession>>([]);

        public Task<IReadOnlyList<UserSession>> FindOtherActiveForIdentityAsync(
            UserIdentityId identityId,
            UserSessionId excluding,
            DateTimeOffset now,
            TimeSpan idleTimeout,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserSession>>([]);

        public Task RecordActivityAsync(
            UserSessionId sessionId,
            DateTimeOffset now,
            TimeSpan staleness,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class BaselinePolicyResolver : ISecurityPolicyResolver
    {
        public Task<SecurityPolicySettings> GetEffectiveSettingsAsync(
            DateTimeOffset at, CancellationToken cancellationToken)
            => Task.FromResult(SecurityBaseline.Current);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
