using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.DeactivateUser;

/// <summary>
/// USR-C4 — DeactivateUser (docs/requirements.md, "USR-C4 / USR-C5").
///
/// Not a status change: one controlled operation that takes the person out of
/// the tenant (UM §11.7, §11.8). In the pipeline's single transaction, under
/// the target's row lock:
///
///   lock and load  →  refuse  →  revoke role assignments (AUT-C2's rule)
///   →  revoke live sessions  →  deactivate identities  →  deactivate the user
///   →  invalidate outstanding tokens  →  declare the audit story
///
/// A refusal or a fault at any step rolls back every step and every record:
/// the unit of work is the only thing that saves, and a handler cannot
/// half-commit. No retries, no after-the-fact cleanup.
///
/// Two attributions, deliberately different (D3). The cascade's revoked
/// sessions and assignments name the System actor in RevokedBy — the
/// deactivation ended them, not a separate decision by a person. The audit
/// records all name the administrator, who is the actor of the operation.
///
/// Authorisation (user.deactivate, human caller) has already run.
/// </summary>
public sealed class DeactivateUserCommandHandler : ICommandHandler<DeactivateUserCommand, DeactivateUserResult>
{
    /// <summary>The assignment's RevocationReason in the cascade (D4).</summary>
    internal const string AssignmentRevocationReason = "User deactivated";

    private readonly IExecutionContext _executionContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IUserTokenRepository _userTokenRepository;
    private readonly IAuditEvents _auditEvents;

    public DeactivateUserCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IUserRoleRepository userRoleRepository,
        IUserSessionRepository userSessionRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IUserTokenRepository userTokenRepository,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(userIdentityRepository);
        ArgumentNullException.ThrowIfNull(userRoleRepository);
        ArgumentNullException.ThrowIfNull(userSessionRepository);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);
        ArgumentNullException.ThrowIfNull(userTokenRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _executionContext = executionContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userIdentityRepository = userIdentityRepository;
        _userRoleRepository = userRoleRepository;
        _userSessionRepository = userSessionRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _userTokenRepository = userTokenRepository;
        _auditEvents = auditEvents;
    }

    public async Task<DeactivateUserResult> Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new BusinessRuleViolationException("A reason is required to deactivate a user.");

        var administrator = _executionContext.UserId;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // Lock first, before anything about this user is read (D6). A
                // grant racing this command waits here, or has committed and
                // is found below.
                var user = await _userRepository.FindForUpdateAsync(command.UserId, ct)
                    ?? throw new BusinessRuleViolationException("The user does not exist.");

                // Read AFTER the lock: an assignment granted while this waited
                // was assigned later than a clock read before it, and
                // UserRole.Revoke refuses a revocation dated before assignment.
                // One instant for the whole cascade, at stored precision.
                var now = RoleAssignmentTime.AtStoredPrecision(_clock.UtcNow);

                if (user.ActorType != ActorType.Human)
                    throw new BusinessRuleViolationException("This user cannot be deactivated.");

                if (user.Status == UserStatus.Inactive)
                    throw new BusinessRuleViolationException("The user is already inactive.");

                var stamp = new DeactivationStamp(now, administrator);
                var userBefore = LifecycleState(user.Status, user.Deactivation);

                try
                {
                    user.Deactivate(stamp);
                }
                catch (DomainException refusal)
                {
                    // D9c — the self rule, stated by the domain.
                    throw new BusinessRuleViolationException(refusal.Message);
                }

                // Step 4 — the AUT-C2 rule, not the catalogue's "EffectiveTo =
                // now" (amended): an active assignment ends now, a future one
                // is closed at its own start. Ended and revoked ones are not
                // passed to Revoke at all; there is nothing left to close.
                var revokedAssignments = new List<(UserRole Assignment, DateTimeOffset? EffectiveToBefore)>();

                foreach (var assignment in await _userRoleRepository.FindForUserAsync(user.Id, ct))
                {
                    if (assignment.StateAt(now) is not (RoleAssignmentState.Active or RoleAssignmentState.Future))
                        continue;

                    var effectiveToBefore = assignment.EffectiveTo;

                    assignment.Revoke(now, User.SystemUserId, AssignmentRevocationReason);

                    revokedAssignments.Add((assignment, effectiveToBefore));
                }

                // Step 5 — every live session, found as SES-C4 finds them.
                var policy = await _securityPolicyResolver.GetEffectiveSettingsAsync(now, ct);

                var revokedSessions = new List<UserSession>();

                foreach (var session in await _userSessionRepository.FindActiveForUserAsync(
                             user.Id, now, policy.SessionIdleTimeout, ct))
                {
                    if (session.Revoke(now, User.SystemUserId, SessionRevocations.UserDeactivated))
                        revokedSessions.Add(session);
                }

                // Step 6 — every active identity, with the user's own stamp, so
                // USR-C5 can tell which ones this operation deactivated (D5).
                var deactivatedIdentities = new List<UserIdentity>();

                foreach (var identity in await _userIdentityRepository.FindByUserIdAsync(user.Id, ct))
                {
                    if (identity.Status != UserStatus.Active)
                        continue;

                    identity.Deactivate(stamp);
                    deactivatedIdentities.Add(identity);
                }

                // Step 7a — D7. State only: the frozen TokenInvalidated
                // requires a SupersededBy token and there is no replacement, so
                // no per-token record is written (D13). UserDeactivated is the
                // record of this operation. Do not fabricate a superseding
                // token and do not weaken the frozen definition to add one.
                await _userTokenRepository.InvalidateOutstandingForUserAsync(user.Id, now, ct);

                // Step 8 — one operation, one story: every cascade record is
                // caused by UserDeactivated (behaviour 18) and carries the
                // administrator's reason.
                var root = _auditEvents.Emit("UserDeactivated", version: 1)
                    .Primary("User", user.Id.Value)
                    .WithBefore(userBefore)
                    .WithAfter(LifecycleState(user.Status, user.Deactivation))
                    .WithReason(command.Reason);

                foreach (var identity in deactivatedIdentities)
                {
                    _auditEvents.Emit("IdentityDeactivated", version: 1)
                        .Primary("Identity", identity.Id.Value)
                        .Ref("User", user.Id.Value, role: "Subject")
                        .WithBefore(LifecycleState(UserStatus.Active, deactivation: null))
                        .WithAfter(LifecycleState(identity.Status, identity.Deactivation))
                        .WithReason(command.Reason)
                        .CausedBy(root);
                }

                foreach (var (assignment, effectiveToBefore) in revokedAssignments)
                {
                    _auditEvents.Emit("RoleRevoked", version: 1)
                        .Primary("UserRoleAssignment", assignment.Id.Value)
                        .Ref("User", assignment.UserId.Value, role: "Subject")
                        .Ref("Role", assignment.RoleId.Value, role: "RevokedRole")
                        .WithBefore(new { effectiveTo = effectiveToBefore })
                        .WithAfter(new
                        {
                            effectiveTo = assignment.EffectiveTo,
                            revokedAt = assignment.RevokedAt,
                        })
                        .WithReason(command.Reason)
                        .CausedBy(root);
                }

                foreach (var session in revokedSessions)
                    SessionRevocations.Declare(_auditEvents, session, user.Id, command.Reason, cause: root);

                // Step 9 — nothing to invalidate: there is no effective-
                // permission cache; permissions are resolved per request (UR12).
                return DeactivateUserResult.Accepted;
            },
            cancellationToken);
    }

    /// <summary>The frozen Before/After of UserDeactivated and IdentityDeactivated.</summary>
    internal static object LifecycleState(UserStatus status, DeactivationStamp? deactivation)
        => new
        {
            Status = status.ToString(),
            DeactivatedAt = deactivation?.At,
            DeactivatedBy = deactivation?.By.Value,
        };
}
