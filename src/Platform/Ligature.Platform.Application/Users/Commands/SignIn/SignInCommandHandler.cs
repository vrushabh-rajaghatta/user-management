using System.Net;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
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
    private readonly IBearerActorEstablisher _bearerActorEstablisher;
    private readonly IAuditEvents _auditEvents;

    public SignInCommandHandler(
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserIdentityRepository userIdentityRepository,
        IUserRepository userRepository,
        ICredentialRepository credentialRepository,
        IUserSessionRepository userSessionRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IPasswordHasher passwordHasher,
        IBearerActorEstablisher bearerActorEstablisher,
        IAuditEvents auditEvents)
    {
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userIdentityRepository = userIdentityRepository;
        _userRepository = userRepository;
        _credentialRepository = credentialRepository;
        _userSessionRepository = userSessionRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _passwordHasher = passwordHasher;
        _bearerActorEstablisher = bearerActorEstablisher;
        _auditEvents = auditEvents;
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

                    // No identity to name: the catalogue does not require one
                    // for this event, precisely so an attempt against a
                    // username nobody holds is still recorded.
                    DeclareFailure(command, identity: null, "IdentityNotUsable");

                    return SignInResult.Failure();
                }

                var credential = resolved.Credential;

                // Step 3. A live lock refuses before the password is even
                // considered — but still pays the decoy cost, so "locked" is
                // not distinguishable from "wrong password" by timing.
                if (credential.LockedUntil is { } lockedUntil && lockedUntil > now)
                {
                    _passwordHasher.VerifyDecoy(command.Password);

                    DeclareFailure(command, resolved.Identity, "AccountLocked");

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
                    var attemptsBefore = credential.FailedAttemptCount;

                    credential.RecordFailedAttempt();

                    DeclareFailure(command, resolved.Identity, "CredentialsRejected");

                    // MaxFailedLoginAttempts is a CAP and LockoutDuration a
                    // FLOOR, both resolved from the effective policy.
                    if (credential.FailedAttemptCount >= policy.MaxFailedLoginAttempts)
                    {
                        credential.Lock(now + policy.LockoutDuration);

                        // The system locked the account, not the person who
                        // mistyped their password: nobody asked for this, a
                        // policy threshold was crossed. Attributing it to the
                        // caller would say they locked themselves out, and
                        // this attempt has no authenticated caller to blame in
                        // any case (AsSystem, and the catalogue permits only a
                        // System origin here).
                        //
                        // Transactional, unlike the attempt above: the lock IS
                        // a state change on the credential row, and the record
                        // of it belongs with the change it describes.
                        _auditEvents.Emit("AccountLocked", version: 1)
                            .AsSystem()
                            .Primary("Credential", credential.Id.Value)
                            .Ref("Identity", resolved.Identity.Id.Value, role: "Target")
                            .Ref("User", resolved.Identity.UserId.Value, role: "Subject")
                            .WithBefore(new
                            {
                                FailedAttemptCount = attemptsBefore,
                                LockedUntil = (DateTimeOffset?)null,
                            })
                            .WithAfter(new
                            {
                                FailedAttemptCount = credential.FailedAttemptCount,
                                LockedUntil = (DateTimeOffset?)credential.LockedUntil,
                            });
                    }

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

                // The password verified, so this caller IS authenticated, and
                // the record of it must say who by name. The identity is the
                // one whose credential just verified — passed through, never
                // looked up again (AUD-D28).
                if (!await _bearerActorEstablisher.EstablishAsync(resolved.Identity.Id, ct))
                    return SignInResult.Failure();

                var sessionId = await CreateSessionAsync(
                    resolved.Identity, command, now, policy, ct);

                _auditEvents.Emit("SignInSucceeded", version: 1)
                    .Primary("Session", sessionId.Value)
                    .Ref("Identity", resolved.Identity.Id.Value, role: "Target")
                    .Ref("User", resolved.Identity.UserId.Value, role: "Subject")
                    .WithPayload(new { IpAddress = command.IpAddress });

                return SignInResult.Success(sessionId);
            },
            cancellationToken);
    }

    /// <summary>
    /// The record of a refused attempt.
    ///
    /// Autonomous, and that is the whole reason this event exists separately:
    /// it describes a failure, so it cannot ride the transaction that failed.
    /// It survives whatever the command does afterwards.
    ///
    /// Anonymous by definition — nobody authenticated — which is the only
    /// origin the catalogue permits for it.
    ///
    /// The attempted identifier is recorded as it was typed. It is declared
    /// PII by the catalogue, so the secret scan does not judge it by shape
    /// (E2b): whether an attempt can be recorded must not depend on how long
    /// somebody's username happens to be.
    ///
    /// The failure category is recorded even though SignInResult withholds it
    /// from the caller. The trail is where the difference between "no such
    /// username" and "wrong password" is allowed to exist; the response is
    /// where it is not.
    /// </summary>
    private void DeclareFailure(
        SignInCommand command,
        UserIdentity? identity,
        string category)
    {
        var declaration = _auditEvents.Emit("SignInFailed", version: 1)
            .WithPayload(new
            {
                AttemptedIdentifier = command.Username,
                IpAddress = command.IpAddress,
                FailureCategory = category,
            });

        // The identity when one was resolved, as the primary rather than as a
        // ref: naming it in both places would repeat the primary entity (AE4).
        if (identity is null)
            declaration.Primary("Identity");
        else
            declaration.Primary("Identity", identity.Id.Value);
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
