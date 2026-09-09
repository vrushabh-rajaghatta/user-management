using System.Net;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.SignIn;

/// <summary>
/// SES-C1 — SignIn, steps 1 to 7.
///
/// BOTH OUTCOMES COMMIT. A failed sign-in is a successful business operation
/// whose result happens to be "no". Throwing on a wrong password would roll the
/// transaction back, taking the incremented attempt counter with it, and
/// lockout would never engage — brute force would be free and every happy-path
/// test would still pass. So the failure path returns a result and the
/// transaction commits the counter.
///
/// Step 8 — issue an access token — is deliberately absent, and stays absent
/// now that section 17 has settled what one is. The Host mints the carrier from
/// the SessionId this returns; nothing about signing keys, headers or token
/// format reaches this handler, which is what keeps sign-in usable outside
/// HTTP.
/// </summary>
public sealed class SignInCommandHandler
    : ICommandHandler<SignInCommand, SignInResult>
{
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICredentialRepository _credentialRepository;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IPasswordHasher _passwordHasher;

    public SignInCommandHandler(
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserIdentityRepository userIdentityRepository,
        IUserRepository userRepository,
        ICredentialRepository credentialRepository,
        IUserSessionRepository userSessionRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IPasswordHasher passwordHasher)
    {
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userIdentityRepository = userIdentityRepository;
        _userRepository = userRepository;
        _credentialRepository = credentialRepository;
        _userSessionRepository = userSessionRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _passwordHasher = passwordHasher;
    }

    public async Task<SignInResult> Handle(
        SignInCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _clock.UtcNow;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var policy = await _securityPolicyResolver
                    .GetEffectiveSettingsAsync(now, ct);

                var resolved = await ResolveAsync(command, ct);

                // Every path that never reached a real credential still pays
                // the derivation cost, so an unknown username cannot be told
                // from a known one by how quickly the answer comes back.
                if (resolved is null)
                {
                    _passwordHasher.VerifyDecoy(command.Password);

                    return SignInResult.Failure();
                }

                var credential = resolved.Credential;

                // Step 3. A live lock refuses before the password is even
                // considered — but still pays the decoy cost, so "locked" is
                // not distinguishable from "wrong password" by timing.
                if (credential.LockedUntil is { } lockedUntil && lockedUntil > now)
                {
                    _passwordHasher.VerifyDecoy(command.Password);

                    return SignInResult.Failure();
                }

                // An EXPIRED lock ends the window it belonged to. Without this,
                // the counter would still sit at the threshold and the next
                // single mistyped password would re-lock immediately for the
                // full duration, forever — one typo costing another lockout
                // with no way for the count to decay.
                if (credential.LockedUntil is not null)
                    credential.Unlock();

                // Step 4. Fixed-time comparison happens inside the hasher; a
                // structurally invalid stored value throws rather than being
                // reported as a wrong password.
                var verification = _passwordHasher.Verify(
                    command.Password,
                    credential.PasswordHash,
                    credential.PasswordAlgorithm);

                if (!verification.IsValid)
                {
                    credential.RecordFailedAttempt();

                    // MaxFailedLoginAttempts is a CAP and LockoutDuration a
                    // FLOOR, both resolved from the effective policy.
                    if (credential.FailedAttemptCount >= policy.MaxFailedLoginAttempts)
                        credential.Lock(now + policy.LockoutDuration);

                    return SignInResult.Failure();
                }

                // Step 5. Same password, newer encoding — before the counters
                // are cleared, so a stale algorithm cannot affect that reset.
                if (verification.NeedsRehash)
                {
                    var rehashed = _passwordHasher.Hash(command.Password);

                    credential.RehashPassword(rehashed.Hash, rehashed.Algorithm);
                }

                // Step 6. Clears FailedAttemptCount and LockedUntil together.
                credential.Unlock();

                // TODO — SES-C1 step 9. Declare SignInSucceeded, SignInFailed
                // or AccountLocked through IAuditEvents, and register the
                // command in AuditDeclarations; the pipeline writes them
                // inside this transaction (docs/architecture.md section 11).
                // SignInFailed and AccountLocked must be emitted
                // even though no session exists, and the reason is known at
                // each return above even though SignInResult deliberately
                // withholds it from the caller.

                var sessionId = await CreateSessionAsync(
                    resolved.Identity, command, now, policy, ct);

                return SignInResult.Success(sessionId);
            },
            cancellationToken);
    }

    /// <summary>
    /// Steps 1 to 3. Returns null for every condition that must be
    /// indistinguishable to the caller: unknown username, inactive identity,
    /// inactive user, and no credential at all (inv. 15, the pending-activation
    /// state).
    /// </summary>
    private async Task<Resolved?> ResolveAsync(
        SignInCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Username))
            return null;

        var identity = await _userIdentityRepository
            .FindLocalByUsernameAsync(command.Username, cancellationToken);

        if (identity is null || identity.Status != UserStatus.Active)
            return null;

        var user = await _userRepository
            .FindAsync(identity.UserId, cancellationToken);

        if (user is null || user.Status != UserStatus.Active)
            return null;

        var credential = await _credentialRepository
            .FindByIdentityAsync(identity.Id, cancellationToken);

        return credential is null ? null : new Resolved(identity, credential);
    }

    /// <summary>
    /// Carries the identity forward so session creation needs no second lookup.
    /// </summary>
    private sealed record Resolved(UserIdentity Identity, Credential Credential);

    /// <summary>
    /// Step 7. ExpiresAt comes from SessionAbsoluteTimeout, a CAP;
    /// LastActivityAt starts at creation.
    /// </summary>
    private async Task<UserSessionId> CreateSessionAsync(
        UserIdentity identity,
        SignInCommand command,
        DateTimeOffset now,
        SecurityPolicySettings policy,
        CancellationToken cancellationToken)
    {
        // Inv. 26 — interactive HUMAN authentication creates a session; machine
        // authentication creates none. Unreachable today because AU11 blocks
        // agent creation, but this is a domain invariant rather than an
        // assumption about what currently exists, and a non-human reaching here
        // is a model violation rather than a failed sign-in.
        if (identity.ActorType != ActorType.Human)
        {
            throw new BusinessRuleViolationException(
                $"Actor type '{identity.ActorType}' cannot hold a session. "
                + "Sessions exist for interactive human authentication only "
                + "(inv. 26); machine actors authenticate per request.");
        }

        var session = UserSession.Create(
            UserSessionId.New(),
            identity.Id,
            now,
            now + policy.SessionAbsoluteTimeout,
            ParseAddress(command.IpAddress),
            command.UserAgent);

        await _userSessionRepository.AddAsync(session, cancellationToken);

        return session.Id;
    }

    /// <summary>
    /// Security evidence, not a security control: an unparseable address is
    /// recorded as absent rather than failing a legitimate sign-in.
    /// </summary>
    private static IPAddress? ParseAddress(string? value)
        => IPAddress.TryParse(value, out var address) ? address : null;
}
