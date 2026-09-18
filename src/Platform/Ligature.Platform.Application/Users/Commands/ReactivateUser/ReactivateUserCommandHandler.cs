using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Users.Commands.DeactivateUser;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.ReactivateUser;

/// <summary>
/// USR-C5 — ReactivateUser (docs/requirements.md, "USR-C4 / USR-C5").
///
/// RESTORES NOTHING (D3, UM §11.7). The user and the identities USR-C4
/// deactivated return to Active; no role assignment, session or token is
/// recreated. Access after a return is granted afresh (AUT-C1), by a named
/// person, with a fresh reason — so it is always traceable to a decision
/// somebody actually made.
///
/// Only identities carrying the user's own deactivation stamp come back (D5):
/// one deactivated separately stays inactive.
///
/// Authorisation (user.reactivate, human caller) has already run.
/// </summary>
public sealed class ReactivateUserCommandHandler : ICommandHandler<ReactivateUserCommand, ReactivateUserResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly IAuditEvents _auditEvents;

    public ReactivateUserCommandHandler(
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(userIdentityRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userIdentityRepository = userIdentityRepository;
        _auditEvents = auditEvents;
    }

    public async Task<ReactivateUserResult> Handle(ReactivateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new BusinessRuleViolationException("A reason is required to reactivate a user.");

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // The same lock USR-C4 and GrantRole take (D6).
                var user = await _userRepository.FindForUpdateAsync(command.UserId, ct)
                    ?? throw new BusinessRuleViolationException("The user does not exist.");

                if (user.ActorType != ActorType.Human)
                    throw new BusinessRuleViolationException("This user cannot be reactivated.");

                if (user.Status == UserStatus.Active || user.Deactivation is null)
                    throw new BusinessRuleViolationException("The user is already active.");

                // AU3: the address may have been reissued while this user was
                // away — the rule exists precisely so it can be. The check is
                // for the message; ux_app_user_active_human_email remains the
                // guarantee, and refuses a race past it. This user is Inactive,
                // so the predicate cannot match their own row.
                if (user.Email is not null
                    && await _userRepository.ExistsActiveHumanWithEmailAsync(user.Email, ct))
                {
                    throw new BusinessRuleViolationException(
                        "Another active user now has this user's email address.");
                }

                var stamp = user.Deactivation;
                var before = DeactivateUserCommandHandler.LifecycleState(user.Status, stamp);

                var reactivated = new List<(UserIdentity Identity, object Before)>();

                foreach (var identity in await _userIdentityRepository.FindByUserIdAsync(user.Id, ct))
                {
                    var identityBefore = DeactivateUserCommandHandler.LifecycleState(identity.Status, identity.Deactivation);

                    if (identity.ReactivateWith(stamp))
                        reactivated.Add((identity, identityBefore));
                }

                user.Reactivate();

                var root = _auditEvents.Emit("UserReactivated", version: 1)
                    .Primary("User", user.Id.Value)
                    .WithBefore(before)
                    .WithAfter(DeactivateUserCommandHandler.LifecycleState(user.Status, user.Deactivation))
                    .WithReason(command.Reason);

                foreach (var (identity, identityBefore) in reactivated)
                {
                    _auditEvents.Emit("IdentityReactivated", version: 1)
                        .Primary("Identity", identity.Id.Value)
                        .Ref("User", user.Id.Value, role: "Subject")
                        .WithBefore(identityBefore)
                        .WithAfter(DeactivateUserCommandHandler.LifecycleState(identity.Status, identity.Deactivation))
                        .WithReason(command.Reason)
                        .CausedBy(root);
                }

                return ReactivateUserResult.Accepted;
            },
            cancellationToken);
    }
}
