using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.GrantRole;

/// <summary>
/// AUT-C1 — GrantRole (docs/requirements.md, "Role Assignment").
///
/// load → validate → domain operation → persist → audit, in the pipeline's
/// transaction. Authorisation (role.grant, human caller) has already run.
///
/// THE DOMAIN OWNS THE PERIOD RULES. No backdating, and a grant that must end
/// strictly after it starts, live in UserRole.Create and are not restated
/// here; their refusal is surfaced unchanged as the ordinary business refusal.
///
/// THE DATABASE OWNS OVERLAP. Two concurrent grants would both pass an
/// application check, so none is made: the exclusion constraints decide at
/// the save, and the translator words the refusal.
///
/// Global only in v1: the command takes no scope. No four-eyes rule (open
/// decision A1): the permission alone decides, including for a self-grant.
/// </summary>
public sealed class GrantRoleCommandHandler : ICommandHandler<GrantRoleCommand, GrantRoleResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IAuditEvents _auditEvents;

    public GrantRoleCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IUserRoleRepository userRoleRepository,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(roleRepository);
        ArgumentNullException.ThrowIfNull(userRoleRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _executionContext = executionContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _auditEvents = auditEvents;
    }

    public async Task<GrantRoleResult> Handle(GrantRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Before any database work. RoleGranted requires a reason, and a
        // missing one would otherwise surface as an emission defect (500).
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new BusinessRuleViolationException("A reason is required to grant a role.");

        var now = RoleAssignmentTime.AtStoredPrecision(_clock.UtcNow);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // An active human. The System actor fails the actor-type
                // check; agents are out of scope in v1 (invariant 17a).
                //
                // Read under the target's row lock, the one USR-C4 and USR-C5
                // take (D6). Checked unlocked, a grant could read Active, a
                // deactivation commit, and the grant then commit a live
                // assignment on an inactive user — dormant until reactivation
                // revived it, the failure UM §11.7 exists to prevent. Whichever
                // command locks first decides the order.
                var target = await _userRepository.FindForUpdateAsync(command.UserId, ct);

                if (target is null
                    || target.ActorType != ActorType.Human
                    || target.Status != UserStatus.Active)
                {
                    throw new BusinessRuleViolationException("A role cannot be granted to this user.");
                }

                // A retired role receives no new assignments (AUT-C5);
                // existing ones are unaffected.
                var role = await _roleRepository.FindAsync(command.RoleId, ct);

                if (role is null || !role.IsActive)
                    throw new BusinessRuleViolationException("This role cannot be granted.");

                var administrator = _executionContext.UserId;

                UserRole assignment;

                try
                {
                    assignment = UserRole.Create(
                        UserRoleId.New(),
                        target.Id,
                        ActorType.Human,
                        role.Id,
                        ScopeType.Global,
                        scopeId: null,
                        effectiveFrom: RoleAssignmentTime.AtStoredPrecision(command.EffectiveFrom) ?? now,
                        effectiveTo: RoleAssignmentTime.AtStoredPrecision(command.EffectiveTo),
                        assignedAt: now,
                        assignedBy: administrator,
                        assignmentReason: command.Reason,
                        createdAt: now,
                        createdBy: administrator);
                }
                catch (DomainException refusal)
                {
                    throw new BusinessRuleViolationException(refusal.Message);
                }

                await _userRoleRepository.AddAsync(assignment, ct);

                _auditEvents.Emit("RoleGranted", version: 1)
                    .Primary("UserRoleAssignment", assignment.Id.Value)
                    .Ref("User", target.Id.Value, role: "Subject")
                    .Ref("Role", role.Id.Value, role: "GrantedRole")
                    .WithAfter(new
                    {
                        scopeType = assignment.ScopeType.Value,
                        effectiveFrom = assignment.EffectiveFrom,
                        effectiveTo = assignment.EffectiveTo,
                    })
                    .WithReason(command.Reason);

                return new GrantRoleResult(assignment.Id);
            },
            cancellationToken);
    }
}
