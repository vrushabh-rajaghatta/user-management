using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Users;

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
            hasher);

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
        public Task<bool> ExistsWithUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(UserIdentity identity, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<UserIdentity?> FindLocalByUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult<UserIdentity?>(null);
    }

    private sealed class EmptyUserRepository : IUserRepository
    {
        public Task<bool> ExistsActiveHumanWithEmailAsync(
            EmailAddress email, CancellationToken cancellationToken)
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
