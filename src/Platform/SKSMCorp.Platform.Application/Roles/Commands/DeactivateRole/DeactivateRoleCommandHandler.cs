using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Roles.Commands.DeactivateRole;

/// <summary>
/// AUT-C5 — DeactivateRole (docs/requirements.md, "AUT-C5 DeactivateRole and
/// AUT-C6 ReactivateRole").
///
/// refuse a blank reason -> load -> refuse the unknown -> the domain refuses a
/// system role and says whether anything changed -> audit only on a real
/// transition, in the pipeline's transaction.
///
/// THE REASON IS CHECKED FIRST, OUTSIDE THE TRANSACTION (G6, RD5). No
/// transaction is opened, so no database work happens — and RoleDeactivated is
/// seeded ReasonRequired, so a blank one reaching the audit assembler would
/// fail AR9 as an emission defect: a 500 instead of a refusal.
///
/// NO ROW LOCK (RD9). Deactivation revokes no assignment and changes no
/// existing authorisation; the only transition is IsActive. A grant may race
/// it, which is a recorded Known Gap rather than a reason to lock.
///
/// WHAT THIS DOES NOT DO: it touches no user_role row. Eligibility for NEW
/// assignments changes; existing holders keep their access, which is the whole
/// point of the story.
/// </summary>
public sealed class DeactivateRoleCommandHandler
    : ICommandHandler<DeactivateRoleCommand, DeactivateRoleResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRoleRepository _roleRepository;
    private readonly IAuditEvents _auditEvents;

    public DeactivateRoleCommandHandler(
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

    public async Task<DeactivateRoleResult> Handle(
        DeactivateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Before any database work (G6).
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new BusinessRuleViolationException("A reason is required to deactivate a role.");

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var role = await _roleRepository.FindTrackedAsync(command.RoleId, ct)
                    ?? throw new BusinessRuleViolationException("The role does not exist.");

                var before = Activity(role);

                bool changed;

                try
                {
                    // Ownership is refused before the no-op check (RD-A5).
                    changed = role.Deactivate();
                }
                catch (DomainException refusal)
                {
                    throw new BusinessRuleViolationException(refusal.Message);
                }

                // RD4 — reaching the state already held writes and records nothing.
                if (changed)
                {
                    _auditEvents.Emit("RoleDeactivated", version: 1)
                        .Primary("Role", role.Id.Value)
                        .WithBefore(before)
                        .WithAfter(Activity(role))
                        .WithReason(command.Reason);
                }

                return new DeactivateRoleResult(
                    role.Id, role.Code, role.Name, role.Description, role.IsSystemRole, role.IsActive);
            },
            cancellationToken);
    }

    /// <summary>Exactly what this command may change (RD7).</summary>
    private static object Activity(Role role)
        => new
        {
            role.IsActive,
        };
}
