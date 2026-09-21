using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Roles.Commands.UpdateRoleMetadata;

/// <summary>
/// AUT-C4 — UpdateRoleMetadata (docs/requirements.md, "AUT-C4
/// UpdateRoleMetadata").
///
/// load -> refuse the unknown -> the domain refuses a system role, normalises,
/// and says whether anything changed -> audit only on a change, in the
/// pipeline's transaction. Authorisation (role.manage, human actors only) has
/// already run.
///
/// NO ROW LOCK (RM5), as USR-C2 takes none: the D6 lock orders commands that
/// depend on lifecycle status, and metadata does not. Last write wins, and RM1
/// introduces no uniqueness that would need ordering.
///
/// DELIBERATELY NARROW. Name and description only: never the code (RM2), never
/// ownership (RM10), never activity — that is AUT-C5/C6 — and never
/// permissions or holders.
/// </summary>
public sealed class UpdateRoleMetadataCommandHandler
    : ICommandHandler<UpdateRoleMetadataCommand, UpdateRoleMetadataResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRoleRepository _roleRepository;
    private readonly IAuditEvents _auditEvents;

    public UpdateRoleMetadataCommandHandler(
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

    public async Task<UpdateRoleMetadataResult> Handle(
        UpdateRoleMetadataCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var role = await _roleRepository.FindTrackedAsync(command.RoleId, ct)
                    ?? throw new BusinessRuleViolationException("The role does not exist.");

                var before = Metadata(role);

                bool changed;

                try
                {
                    // The domain refuses a release-owned role before anything
                    // else (RM4), so identical values do not read as a no-op.
                    changed = role.UpdateMetadata(command.Name, command.Description);
                }
                catch (DomainException refusal)
                {
                    // The rules are the domain's; their wording reaches the
                    // caller through the application's refusal, as USR-C2 does.
                    throw new BusinessRuleViolationException(refusal.Message);
                }

                // RM3 — no change after normalisation writes and records
                // nothing. The tracker has nothing to save either.
                if (changed)
                {
                    _auditEvents.Emit("RoleUpdated", version: 1)
                        .Primary("Role", role.Id.Value)
                        .WithBefore(before)
                        .WithAfter(Metadata(role));
                }

                // RM6 — the role AS STORED, change or not.
                return new UpdateRoleMetadataResult(
                    role.Id,
                    role.Code,
                    role.Name,
                    role.Description,
                    role.IsSystemRole,
                    role.IsActive);
            },
            cancellationToken);
    }

    /// <summary>Exactly what this command may change, and nothing else.</summary>
    private static object Metadata(Role role)
        => new
        {
            role.Name,
            role.Description,
        };
}
