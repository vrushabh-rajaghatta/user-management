using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Application.Users.Commands.SignIn;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Tests.Users;

/// <summary>
/// The decoy verification is a timing mitigation, and timing is not reliably
/// observable in a test. What IS observable is whether the handler asks for it,
/// so this asserts the call with a spy rather than trying to measure
/// microseconds (SES-C1).
///
/// Without it, removing the decoy from the not-found path passes every other
/// test while quietly reopening the username-existence oracle.
/// </summary>
public sealed class SignInDecoyTests
{
    [Fact]
    public async Task An_unknown_username_still_performs_a_verification()
    {
        var hasher = new SpyPasswordHasher();

        var result = await HandlerWith(hasher).Handle(
            new SignInCommand("nobody", "some-password", null, null),
            CancellationToken.None);

        Assert.False(result.Succeeded);

        Assert.True(
            hasher.DecoyCalls == 1,
            "An unknown username must still pay the derivation cost, or how "
            + "quickly the answer returns reveals whether the account exists.");

        // And no real verification happened — there was nothing to verify.
        Assert.Equal(0, hasher.VerifyCalls);
    }

    private static SignInCommandHandler HandlerWith(IPasswordHasher hasher)
        => new(
            new FixedClock(),
            new PassThroughUnitOfWork(),
            new EmptyIdentityRepository(),
            new EmptyUserRepository(),
            new EmptyCredentialRepository(),
            new NullSessionRepository(),
            new BaselinePolicyResolver(),
            hasher,
            new UnusedEstablisher(),
            OpenCommand());

    /// <summary>
    /// This suite is about the decoy, and the decoy paths never authenticate,
    /// so nothing here should ever establish a caller. Refusing outright is
    /// what makes that an assertion rather than an assumption.
    /// </summary>
    private sealed class UnusedEstablisher : IBearerActorEstablisher
    {
        public Task<bool> EstablishAsync(
            UserIdentityId identityId, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "A failed sign-in must not establish a caller.");
    }

    /// <summary>
    /// A collector with a command open, because the handler declares
    /// SignInFailed on the paths this suite drives.
    /// </summary>
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

    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
            => operation(cancellationToken);
    }

    private sealed class EmptyIdentityRepository : IUserIdentityRepository
    {
        public Task<IReadOnlyList<UserIdentity>> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsWithUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(UserIdentity identity, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<UserIdentity?> FindLocalByUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult<UserIdentity?>(null);

        public Task<IReadOnlyList<UserIdentityId>> FindPasswordResetCandidatesAsync(
            string emailOrUsername, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserIdentityId>>([]);

        public Task<IReadOnlyList<UserIdentity>> FindLocalByUserIdAsync(
            UserId userId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserIdentity>>([]);

        public Task<UserIdentity?> FindAsync(
            UserIdentityId userIdentityId, CancellationToken cancellationToken)
            => Task.FromResult<UserIdentity?>(null);
    }

    private sealed class EmptyUserRepository : IUserRepository
    {
        public Task<User?> FindForUpdateAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsActiveHumanWithEmailAsync(
            EmailAddress email, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> ExistsOtherActiveHumanWithEmailAsync(
            EmailAddress email, UserId excluding, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(User user, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<User?> FindAsync(UserId userId, CancellationToken cancellationToken)
            => Task.FromResult<User?>(null);
    }

    private sealed class EmptyCredentialRepository : ICredentialRepository
    {
        public Task AddAsync(Credential credential, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Credential?> FindByIdentityAsync(
            UserIdentityId userIdentityId, CancellationToken cancellationToken)
            => Task.FromResult<Credential?>(null);
    }

    private sealed class NullSessionRepository : IUserSessionRepository
    {
        public Task AddAsync(UserSession session, CancellationToken cancellationToken)
            => Task.CompletedTask;

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
        public DateTimeOffset UtcNow { get; } =
            new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
