using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Commands.ReactivateRole;

/// <summary>AUT-C5/C6 — see the contract. Not implemented yet.</summary>
public sealed class ReactivateRoleCommandHandler
    : ICommandHandler<ReactivateRoleCommand, ReactivateRoleResult>
{
    public ReactivateRoleCommandHandler(
        IUnitOfWork unitOfWork,
        IRoleRepository roleRepository,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(roleRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);
    }

    public Task<ReactivateRoleResult> Handle(ReactivateRoleCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-C5/C6.");
}
