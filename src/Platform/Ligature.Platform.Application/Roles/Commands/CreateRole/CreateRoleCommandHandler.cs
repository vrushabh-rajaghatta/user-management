using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Roles.Commands.CreateRole;

/// <summary>
/// AUT-C3 — CreateRole (docs/requirements.md, "AUT-C3 CreateRole"). The first
/// application write path for roles, and it creates TENANT roles only.
///
///   validate  ->  refuse a colliding code  ->  create  ->  RoleCreated
///
/// RC9: the command carries no IsSystemRole and this handler never passes one.
/// Role.Create is told false, so a release-owned role cannot be made here
/// whatever a caller sends — which is what PRV-C2 Amendment 1 assumes when it
/// leaves tenant roles outside catalogue reconciliation.
///
/// THE COLLISION IS CHECKED IGNORING CASE, because the database's index is
/// case-sensitive: without this, "quality-reviewer" and "Quality-Reviewer"
/// would be two roles. The index remains the guarantee, and is translated to
/// the same sentence, so a race past this check reads identically.
///
/// Authorisation (role.manage, human caller) has already run in the pipeline.
/// </summary>
public sealed class CreateRoleCommandHandler : ICommandHandler<CreateRoleCommand, CreateRoleResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRoleRepository _roleRepository;
    private readonly IAuditEvents _auditEvents;

    public CreateRoleCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IRoleRepository roleRepository,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(roleRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _executionContext = executionContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _roleRepository = roleRepository;
        _auditEvents = auditEvents;
    }

    public async Task<CreateRoleResult> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // Step 1 — the domain's own rules, before any lookup: an
                // invalid code is refused for what it is, never queried.
                var role = Role.Create(
                    RoleId.New(),
                    command.Name,
                    command.Code,
                    command.Description,
                    isSystemRole: false,
                    _clock.UtcNow,
                    _executionContext.UserId);

                // Step 2 — RC1 and RC5: one sentence for an exact clash and a
                // case-only one alike, so the API never exposes the database's
                // case semantics.
                if (await _roleRepository.ExistsWithCodeAsync(role.Code, ct))
                    throw new BusinessRuleViolationException("A role with this code already exists.");

                await _roleRepository.AddAsync(role, ct);

                // Step 3 — RC6: After only, and no IsSystemRole. It is a fixed
                // invariant of this command, not change information.
                _auditEvents.Emit("RoleCreated", version: 1)
                    .Primary("Role", role.Id.Value)
                    .WithAfter(new
                    {
                        role.Code,
                        role.Name,
                        role.Description,
                    });

                return new CreateRoleResult(
                    role.Id, role.Code, role.Name, role.Description, role.IsSystemRole, role.IsActive);
            },
            cancellationToken);
    }
}
