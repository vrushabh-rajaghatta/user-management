using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Roles.Commands.ReactivateRole;

/// <summary>
/// AUT-C6 — ReactivateRole, AUT-C5's inverse, with the same shape and one
/// difference: NO REASON (RD5). The catalogue gives this command none and
/// RoleReactivated is seeded ReasonRequired: false, so there is nothing to
/// check before the transaction.
/// </summary>
public sealed class ReactivateRoleCommandHandler
    : ICommandHandler<ReactivateRoleCommand, ReactivateRoleResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRoleRepository _roleRepository;
    private readonly IAuditEvents _auditEvents;

    public ReactivateRoleCommandHandler(
        IUnitOfWork unitOfWork,
        IRoleRepository roleRepository,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(roleRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _unitOfWork = unitOfWork;
        _roleRepository = roleRepository;
        _auditEvents = auditEvents;
    }

    public async Task<ReactivateRoleResult> Handle(
        ReactivateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var role = await _roleRepository.FindTrackedAsync(command.RoleId, ct)
                    ?? throw new BusinessRuleViolationException("The role does not exist.");

                var before = Activity(role);

                bool changed;

                try
                {
                    changed = role.Reactivate();
                }
                catch (DomainException refusal)
                {
                    throw new BusinessRuleViolationException(refusal.Message);
                }

                if (changed)
                {
                    _auditEvents.Emit("RoleReactivated", version: 1)
                        .Primary("Role", role.Id.Value)
                        .WithBefore(before)
                        .WithAfter(Activity(role));
                }

                return new ReactivateRoleResult(
                    role.Id, role.Code, role.Name, role.Description, role.IsSystemRole, role.IsActive);
            },
            cancellationToken);
    }

    private static object Activity(Role role)
        => new
        {
            role.IsActive,
        };
}
