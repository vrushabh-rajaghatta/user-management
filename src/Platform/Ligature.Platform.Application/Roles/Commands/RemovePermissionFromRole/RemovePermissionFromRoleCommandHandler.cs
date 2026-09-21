using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Roles.Commands.RemovePermissionFromRole;

/// <summary>
/// AUT-C8 — RemovePermissionFromRole.
///
/// reason -> grant -> ownership -> close -> audit. The row is CLOSED, never
/// deleted (invariant 11): what the role could do, and when, stays answerable.
///
/// THE REASON IS CHECKED FIRST, OUTSIDE THE TRANSACTION (G6, RG5).
/// PermissionRevokedFromRole is seeded ReasonRequired, so a blank one reaching
/// the audit assembler would fail AR9 as an emission defect — a 500 instead of
/// a refusal. It is not persisted on the row: the frozen entity has no column
/// for it, so the audit record is where it lives.
/// </summary>
public sealed class RemovePermissionFromRoleCommandHandler
    : ICommandHandler<RemovePermissionFromRoleCommand, RemovePermissionFromRoleResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRoleRepository _roleRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IClock _clock;
    private readonly IExecutionContext _executionContext;
    private readonly IAuditEvents _auditEvents;

    public RemovePermissionFromRoleCommandHandler(
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

    public async Task<RemovePermissionFromRoleResult> Handle(
        RemovePermissionFromRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Before any database work (G6).
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new BusinessRuleViolationException("A reason is required to revoke a permission from a role.");

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var grant = await _rolePermissionRepository.FindTrackedAsync(command.RolePermissionId, ct)
                    ?? throw new BusinessRuleViolationException("The role permission does not exist.");

                var role = await _roleRepository.FindAsync(grant.RoleId, ct)
                    ?? throw new BusinessRuleViolationException("The role does not exist.");

                // The grant's role decides ownership (RG1): revoking a seeded
                // grant would refuse every later deployment as
                // RevokedGrantInSeed, which PRV-C2 may not undo (F6).
                if (role.IsSystemRole)
                    throw new BusinessRuleViolationException("System roles cannot be modified.");

                var permission = await _permissionRepository.FindAsync(grant.PermissionId, ct)
                    ?? throw new BusinessRuleViolationException("The permission does not exist.");

                var before = new { IsActive = grant.IsActive };

                try
                {
                    grant.Revoke(_clock.UtcNow, _executionContext.UserId);
                }
                catch (DomainException refusal)
                {
                    throw new BusinessRuleViolationException(refusal.Message);
                }

                _auditEvents.Emit("PermissionRevokedFromRole", version: 1)
                    .Primary("RolePermissionGrant", grant.Id.Value)
                    .Ref("Role", role.Id.Value, role: "Target")
                    .Ref("Permission", permission.PermissionId.Value, role: "Revoked")
                    .WithBefore(before)
                    .WithAfter(new { IsActive = grant.IsActive })
                    .WithReason(command.Reason);

                return RemovePermissionFromRoleResult.Accepted;
            },
            cancellationToken);
    }
}
