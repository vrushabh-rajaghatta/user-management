using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Roles.Commands.AddPermissionToRole;

/// <summary>
/// AUT-C7 — AddPermissionToRole (docs/requirements.md, "AUT-C7
/// AddPermissionToRole and AUT-C8 RemovePermissionFromRole").
///
/// role -> ownership -> permission -> live grant -> RP6 -> insert -> audit,
/// all in the pipeline's transaction. Ownership comes before everything, as
/// AUT-C4 and AUT-C5 order it, because a release-owned role is not this
/// command's to touch whatever else is true.
///
/// RP6 IS THE RULE THE CATALOGUE CALLS MOST LIKELY TO BE OMITTED. A role held
/// by an agent may not acquire a permission that requires a human actor, and
/// the refusal carries the remediation because the catalogue requires it IN
/// the message.
///
/// WHAT THIS CANNOT PROMISE (RG2). The check reads user_role while GrantRole
/// writes it, and the two share no row and no constraint, so under concurrent
/// writes the application cannot guarantee RP6 atomically. Closing that needs
/// a role lock in both commands, which would amend AUT-C1's frozen contract to
/// prevent a race that cannot occur while agents cannot be created. Recorded
/// as a Known Gap rather than quietly half-solved.
/// </summary>
public sealed class AddPermissionToRoleCommandHandler
    : ICommandHandler<AddPermissionToRoleCommand, AddPermissionToRoleResult>
{
    private const string HumanOnlyRefusal =
        "This role is held by an agent, so it cannot be given a permission that requires a human actor. "
        + "Revoke the agent's assignment, add the permission, then grant the agent an agent-safe role.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IRoleRepository _roleRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IClock _clock;
    private readonly IExecutionContext _executionContext;
    private readonly IAuditEvents _auditEvents;

    public AddPermissionToRoleCommandHandler(
        IUnitOfWork unitOfWork,
        IRoleRepository roleRepository,
        IRolePermissionRepository rolePermissionRepository,
        IPermissionRepository permissionRepository,
        IUserRoleRepository userRoleRepository,
        IClock clock,
        IExecutionContext executionContext,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(roleRepository);
        ArgumentNullException.ThrowIfNull(rolePermissionRepository);
        ArgumentNullException.ThrowIfNull(permissionRepository);
        ArgumentNullException.ThrowIfNull(userRoleRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _unitOfWork = unitOfWork;
        _roleRepository = roleRepository;
        _rolePermissionRepository = rolePermissionRepository;
        _permissionRepository = permissionRepository;
        _userRoleRepository = userRoleRepository;
        _clock = clock;
        _executionContext = executionContext;
        _auditEvents = auditEvents;
    }

    public async Task<AddPermissionToRoleResult> Handle(
        AddPermissionToRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var role = await _roleRepository.FindAsync(command.RoleId, ct)
                    ?? throw new BusinessRuleViolationException("The role does not exist.");

                // Ownership first (RG1): a release-owned role's grants are the
                // release's, and PRV-C2 refuses every later deployment if one
                // appears that the seed does not list.
                if (role.IsSystemRole)
                    throw new BusinessRuleViolationException("System roles cannot be modified.");

                var permission = await _permissionRepository.FindAsync(command.PermissionId, ct)
                    ?? throw new BusinessRuleViolationException("The permission does not exist.");

                // RG4: a new authorization edge is only created against the
                // CURRENT catalogue.
                if (!permission.IsActive)
                    throw new BusinessRuleViolationException("The permission is not active.");

                // RP2's pre-check. The index remains the guarantee, and is
                // mapped to this same sentence.
                if (await _rolePermissionRepository.FindLiveAsync(role.Id, permission.PermissionId, ct) is not null)
                    throw new BusinessRuleViolationException("The role already has this permission.");

                var now = _clock.UtcNow;

                // RP6, the two-edge check's second edge.
                if (permission.RequiresHumanActor
                    && await _userRoleRepository.HasActiveAgentAssignmentAsync(role.Id, now, ct))
                {
                    throw new BusinessRuleViolationException(HumanOnlyRefusal);
                }

                // A NEW row, never an update of a revoked one (RP1).
                var grant = RolePermission.Create(
                    RolePermissionId.New(), role.Id, permission.PermissionId, now, _executionContext.UserId);

                await _rolePermissionRepository.AddAsync(grant, ct);

                _auditEvents.Emit("PermissionGrantedToRole", version: 1)
                    .Primary("RolePermissionGrant", grant.Id.Value)
                    .Ref("Role", role.Id.Value, role: "Target")
                    .Ref("Permission", permission.PermissionId.Value, role: "Granted")
                    .WithAfter(new
                    {
                        role.Code,
                        Permission = permission.Code,
                    });

                return new AddPermissionToRoleResult(grant.Id);
            },
            cancellationToken);
    }
}
