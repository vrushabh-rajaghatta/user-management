using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Commands.AddPermissionToRole;

/// <summary>AUT-C7/C8 — see the contract. Not implemented yet.</summary>
public sealed class AddPermissionToRoleCommandHandler
    : ICommandHandler<AddPermissionToRoleCommand, AddPermissionToRoleResult>
{
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
    }

    public Task<AddPermissionToRoleResult> Handle(AddPermissionToRoleCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-C7/C8.");
}
