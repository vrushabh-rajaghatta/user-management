using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Commands.RemovePermissionFromRole;

/// <summary>AUT-C7/C8 — see the contract. Not implemented yet.</summary>
public sealed class RemovePermissionFromRoleCommandHandler
    : ICommandHandler<RemovePermissionFromRoleCommand, RemovePermissionFromRoleResult>
{
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
    }

    public Task<RemovePermissionFromRoleResult> Handle(RemovePermissionFromRoleCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-C7/C8.");
}
