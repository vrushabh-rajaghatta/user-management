using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.ChangePassword;

/// <summary>
/// CRD-C4 — ChangePassword.
///
/// THE PASSWORD OF THE IDENTITY THAT OWNS THIS SESSION (D4). Not "the user's
/// local identity": the session names exactly which identity authenticated, and
/// its ownership by the caller is checked here, as SES-C2 checks it, because
/// "self" is a relationship no pipeline permission expresses.
///
/// NOT A PROBE OF THE CURRENT PASSWORD. The frozen rule is constant-time
/// comparison and a generic error, so a wrong current password and a locked
/// credential share one message, and a locked credential still pays the
/// derivation cost. The current password is verified BEFORE anything about the
/// new one is judged, so whether the new password was acceptable never tells
/// the caller anything about the current one.
///
/// NO LOCKOUT COUNTING (D2). A wrong current password does not touch
/// FailedAttemptCount and cannot lock the account, and CRD-C4 never produces
/// AccountLocked: SES-C1 remains its only producer.
///
/// THE SESSION IS LIMITED INSTEAD (docs/requirements.md, "CRD-C4 — limiting
/// current-password attempts per session", L1–L8). A wrong current password,
/// and a refusal because the credential is locked (L8), count against THIS
/// session. Below the effective MaxFailedLoginAttempts the answer is the
/// uniform refusal; at it, this session is revoked and nothing else is touched.
/// Counted refusals RETURN an outcome rather than throw, because a thrown
/// refusal rolls back, and the count must commit although the command refuses.
/// Every other refusal still throws.
///
/// ONE SERIALISATION POINT. The caller's app_user row lock is taken FIRST, the
/// clock is read after it, and the session is read and re-checked under it
/// (docs/architecture.md, "Serialising commands on one user"). Two attempts on
/// one session therefore cannot both see N−1, and an attempt that waited acts
/// on what committed while it waited, never on what it saw before.
///
/// A LOCKED CREDENTIAL CANNOT BE CHANGED (D3). Otherwise a stolen session would
/// be a way around lockout, since a successful change clears the lock.
///
/// OTHER SESSIONS END (A5 = b1). Every other session of this identity that
/// would still pass the per-request check is revoked, each as its own
/// SessionRevoked caused by the PasswordChanged record; the session making the
/// request survives. Sessions of the user's other identities are untouched —
/// this password could not have created them.
/// </summary>
public sealed class ChangePasswordCommandHandler
    : ICommandHandler<ChangePasswordCommand, ChangePasswordResult>
{
    /// <summary>
    /// One message for a wrong current password, a locked credential, and a
    /// session this command cannot act for. The wording is not a frozen
    /// contract; its uniformity is.
    /// </summary>
    private const string NotChanged = ChangePasswordResult.NotChanged;

    private const string RevocationReason = "PasswordChanged";

    /// <summary>The audit Reason when this session is ended by the limit (L5, R1).</summary>
    internal const string AttemptLimitExplanation =
        "Too many incorrect current passwords while changing the password";

    private readonly IClock _clock;
    private readonly IExecutionContext _executionContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly ICredentialRepository _credentialRepository;
    private readonly IPasswordHistoryRepository _passwordHistoryRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditEvents _auditEvents;

    public ChangePasswordCommandHandler(
        IClock clock,
        IExecutionContext executionContext,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserSessionRepository userSessionRepository,
        IUserIdentityRepository userIdentityRepository,
        ICredentialRepository credentialRepository,
        IPasswordHistoryRepository passwordHistoryRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IPasswordHasher passwordHasher,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(userSessionRepository);
        ArgumentNullException.ThrowIfNull(userIdentityRepository);
        ArgumentNullException.ThrowIfNull(credentialRepository);
        ArgumentNullException.ThrowIfNull(passwordHistoryRepository);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _clock = clock;
        _executionContext = executionContext;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userSessionRepository = userSessionRepository;
        _userIdentityRepository = userIdentityRepository;
        _credentialRepository = credentialRepository;
        _passwordHistoryRepository = passwordHistoryRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _passwordHasher = passwordHasher;
        _auditEvents = auditEvents;
    }

    public async Task<ChangePasswordResult> Handle(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var caller = _executionContext.UserId;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // ---- Step 0. The serialisation point, then the clock. Nothing
                // about the session is read before the lock is held.

                if (await _userRepository.FindForUpdateAsync(caller, ct) is null)
                    throw new BusinessRuleViolationException(NotChanged);

                var now = _clock.UtcNow;

                var policy = await _securityPolicyResolver
                    .GetEffectiveSettingsAsync(now, ct);

                // ---- Step 1. The session, its identity, and the credential.

                var session = await _userSessionRepository
                    .FindAsync(command.CurrentSessionId, ct);

                if (session is null)
                    throw new BusinessRuleViolationException(NotChanged);

                var identity = await _userIdentityRepository
                    .FindAsync(session.UserIdentityId, ct);

                // Ownership, then the identity's kind and state. Without the
                // ownership check a caller could name another user's session
                // and change that user's password with their current one.
                if (identity is null
                    || identity.UserId != caller
                    || identity.IdentityType != IdentityType.Local
                    || identity.Status != UserStatus.Active)
                {
                    throw new BusinessRuleViolationException(NotChanged);
                }

                // Re-checked UNDER the lock, by the pipeline's own definition
                // of active (the repository's, with its tolerance). Ended while
                // this request waited — by the limit, a sign-out, a revocation —
                // it is over: nothing is counted and nothing changes.
                if (await _userSessionRepository.FindActiveAsync(
                        session.Id, now, policy.SessionIdleTimeout, ct) is null)
                {
                    return ChangePasswordResult.SessionEnded;
                }

                var credential = await _credentialRepository
                    .FindByIdentityAsync(identity.Id, ct);

                if (credential is null)
                    throw new BusinessRuleViolationException(NotChanged);

                // ---- Step 2. A live lock refuses, at the cost of a real
                // derivation, so "locked" is not tellable from "wrong" by time.

                if (credential.LockedUntil is { } lockedUntil && lockedUntil > now)
                {
                    _passwordHasher.VerifyDecoy(command.CurrentPassword ?? string.Empty);

                    // L8: counted exactly as a wrong password is, so the count
                    // is not one more way to tell "locked" from "wrong".
                    return CountFailedAttempt(session, caller, now, policy);
                }

                // ---- Step 3. The current password, before anything else is
                // judged. Fixed-time comparison happens inside the hasher. The
                // credential's counter never moves (D2); the session's does.

                var verification = _passwordHasher.Verify(
                    command.CurrentPassword ?? string.Empty,
                    credential.PasswordHash,
                    credential.PasswordAlgorithm);

                if (!verification.IsValid)
                    return CountFailedAttempt(session, caller, now, policy);

                // ---- Step 4. The new password: the policy floor, then reuse.
                // A refusal here proves the current password was right, but is
                // not a success: it neither counts nor resets (thrown, so
                // nothing commits).

                if (command.NewPassword is null
                    || command.NewPassword.Length < policy.PasswordMinLength)
                {
                    throw new BusinessRuleViolationException(
                        "The password must be at least "
                        + $"{policy.PasswordMinLength} characters.");
                }

                // Each row verified with ITS OWN stored hash, as CRD-C3 does.
                // The current password is already in history (CR5), so choosing
                // it again is refused here without a separate rule.
                var recent = await _passwordHistoryRepository.FindRecentAsync(
                    identity.Id, policy.PasswordHistoryDepth, ct);

                foreach (var previous in recent)
                {
                    if (_passwordHasher.Verify(
                            command.NewPassword,
                            previous.PasswordHash,
                            previous.PasswordAlgorithm).IsValid)
                    {
                        throw new BusinessRuleViolationException(
                            "The new password must not match a recently used password.");
                    }
                }

                // ---- Step 5. Change, and history in the same transaction.

                var hashed = _passwordHasher.Hash(command.NewPassword);

                // Clears MustChangePassword, as the catalogue requires. The
                // counters it also clears are known to be below the threshold:
                // a live lock was refused above.
                credential.ChangePassword(
                    hashed.Hash,
                    hashed.Algorithm,
                    now,
                    mustChangePassword: false);

                // L3: a successful change starts this session's count again.
                session.ResetFailedPasswordChangeAttempts();

                await _passwordHistoryRepository.AddAsync(
                    PasswordHistory.Create(
                        PasswordHistoryId.New(),
                        identity.Id,
                        hashed.Hash,
                        hashed.Algorithm,
                        createdAt: now),
                    ct);

                // ---- Step 6. End this identity's other sessions (A5).

                var others = await _userSessionRepository.FindOtherActiveForIdentityAsync(
                    identity.Id, session.Id, now, policy.SessionIdleTimeout, ct);

                // Revoke returns false for a session already revoked, which the
                // query excludes; the check still decides what is recorded, so
                // a record never describes a revocation that did not happen.
                var revoked = new List<UserSession>();

                foreach (var other in others)
                {
                    if (other.Revoke(now, caller, RevocationReason))
                        revoked.Add(other);
                }

                // ---- Step 7. Declarations.

                // Never the hash. otherSessionsRevoked is a fact about this
                // change a reviewer needs; the sessions themselves are named by
                // the records below.
                var changed = _auditEvents.Emit("PasswordChanged", version: 1)
                    .Primary("Credential", credential.Id.Value)
                    .Ref("Identity", identity.Id.Value, role: "Target")
                    .WithPayload(new
                    {
                        algorithm = hashed.Algorithm,
                        otherSessionsRevoked = revoked.Count > 0,
                    });

                // One record per session, each naming the change that caused it
                // (behaviour 18). The same revocation columns SES-C2 writes,
                // with a different reason.
                foreach (var other in revoked)
                {
                    _auditEvents.Emit("SessionRevoked", version: 1)
                        .Primary("Session", other.Id.Value)
                        .Ref("Identity", identity.Id.Value, role: "Target")
                        .Ref("User", caller.Value, role: "Subject")
                        .WithBefore(new
                        {
                            RevokedAt = (DateTimeOffset?)null,
                            RevokedBy = (Guid?)null,
                            RevocationReason = (string?)null,
                        })
                        .WithAfter(new
                        {
                            RevokedAt = (DateTimeOffset?)now,
                            RevokedBy = (Guid?)caller.Value,
                            RevocationReason = (string?)RevocationReason,
                        })
                        .WithReason(RevocationReason)
                        .CausedBy(changed);
                }

                return ChangePasswordResult.Changed;
            },
            cancellationToken);
    }

    /// <summary>
    /// One counted refusal (L1–L5). Below the effective MaxFailedLoginAttempts —
    /// deliberately the sign-in threshold, applied here to consecutive failures
    /// within ONE session (L2) — the uniform refusal. At it, this session is
    /// revoked with the controlled code and recorded once as SessionRevoked, by
    /// the account holder, with the explanation as its Reason and the code in
    /// Before/After (R1). Returned, never thrown, so the count commits.
    /// </summary>
    private ChangePasswordResult CountFailedAttempt(
        UserSession session, UserId caller, DateTimeOffset now, SecurityPolicySettings policy)
    {
        if (session.RecordFailedPasswordChange() < policy.MaxFailedLoginAttempts)
            return ChangePasswordResult.Refused;

        if (session.Revoke(now, caller, SessionRevocations.PasswordChangeAttemptsExceeded))
            SessionRevocations.Declare(_auditEvents, session, caller, AttemptLimitExplanation);

        return ChangePasswordResult.SessionEnded;
    }
}
