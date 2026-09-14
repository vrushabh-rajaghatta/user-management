using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.UnlockAccount;

/// <summary>
/// CRD-C6 — UnlockAccount.
///
/// AUTHORISATION FIRST. The pipeline refuses a caller without user.unlock, or a
/// non-human one, before this handler runs, so nothing about the target —
/// whether it exists, whether it is locked — is observable to anyone not
/// already entitled to unlock accounts.
///
/// ONE IDENTITY, EXACTLY THE ONE NAMED. The target is resolved by
/// UserIdentityId and must be a Local, Human, Active identity owned by an
/// Active user, with a credential. Local and Human are checked explicitly
/// rather than inferred from "a credential exists": the schema does not pin
/// credential.identity_type to 'Local', and agent creation is refused only by
/// the application (AU11).
///
/// A LIVE LOCK ONLY (D1). Locked means LockedUntil > now — the test sign-in uses
/// to refuse. An expired lock is already cleared by the next sign-in attempt, so
/// "unlocking" it would record a change no user could notice; it is refused as
/// not locked, as is a credential with failures counted but no lock (D2).
///
/// NEVER ONESELF (D4). A valid session must not be a path around lockout — the
/// boundary CRD-C4 set for password changes, held here for administrators. A
/// locked administrator is unlocked by another, or waits out the lock.
///
/// It unlocks and nothing else: no password, MustChangePassword, session or
/// token change.
/// </summary>
public sealed class UnlockAccountCommandHandler
    : ICommandHandler<UnlockAccountCommand, UnlockAccountResult>
{
    // Admin-facing; the caller is authorised over accounts, so enumeration is
    // not a concern. None of the wording is a frozen contract.
    private const string NotEligible = "This account cannot be unlocked.";
    private const string NotLocked = "This account is not currently locked.";
    private const string OwnAccount = "An administrator cannot unlock their own account.";

    private readonly IClock _clock;
    private readonly IExecutionContext _executionContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICredentialRepository _credentialRepository;
    private readonly IAuditEvents _auditEvents;

    public UnlockAccountCommandHandler(
        IClock clock,
        IExecutionContext executionContext,
        IUnitOfWork unitOfWork,
        IUserIdentityRepository userIdentityRepository,
        IUserRepository userRepository,
        ICredentialRepository credentialRepository,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userIdentityRepository);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(credentialRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _clock = clock;
        _executionContext = executionContext;
        _unitOfWork = unitOfWork;
        _userIdentityRepository = userIdentityRepository;
        _userRepository = userRepository;
        _credentialRepository = credentialRepository;
        _auditEvents = auditEvents;
    }

    public async Task<UnlockAccountResult> Handle(
        UnlockAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Before any database work, on the ordinary 400 surface — a missing
        // reason must not first surface as an audit-assembly defect.
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new BusinessRuleViolationException(
                "A reason is required to unlock an account.");
        }

        var now = _clock.UtcNow;
        var caller = _executionContext.UserId;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // ---- The target (D3). Every check reads the state the change
                // is applied to, inside this transaction, before any write.

                var identity = await _userIdentityRepository
                    .FindAsync(command.UserIdentityId, ct);

                if (identity is null
                    || identity.IdentityType != IdentityType.Local
                    || identity.ActorType != ActorType.Human
                    || identity.Status != UserStatus.Active)
                {
                    throw new BusinessRuleViolationException(NotEligible);
                }

                var user = await _userRepository.FindAsync(identity.UserId, ct);

                if (user is null || user.Status != UserStatus.Active)
                    throw new BusinessRuleViolationException(NotEligible);

                // ---- Never oneself (D4).

                if (identity.UserId == caller)
                    throw new BusinessRuleViolationException(OwnAccount);

                var credential = await _credentialRepository
                    .FindByIdentityAsync(identity.Id, ct);

                if (credential is null)
                    throw new BusinessRuleViolationException(NotEligible);

                // ---- A live lock only (D1, D2).

                if (credential.LockedUntil is not { } lockedUntil || lockedUntil <= now)
                    throw new BusinessRuleViolationException(NotLocked);

                // ---- Unlock, recording exactly what was cleared.

                var attemptsBefore = credential.FailedAttemptCount;

                credential.Unlock();

                _auditEvents.Emit("AccountUnlocked", version: 1)
                    .Primary("Credential", credential.Id.Value)
                    .Ref("Identity", identity.Id.Value, role: "Target")
                    .Ref("User", identity.UserId.Value, role: "Subject")
                    .WithBefore(new
                    {
                        LockedUntil = (DateTimeOffset?)lockedUntil,
                        FailedAttemptCount = attemptsBefore,
                    })
                    .WithAfter(new
                    {
                        LockedUntil = credential.LockedUntil,
                        FailedAttemptCount = credential.FailedAttemptCount,
                    })
                    .WithReason(command.Reason);

                return UnlockAccountResult.Unlocked;
            },
            cancellationToken);
    }
}
