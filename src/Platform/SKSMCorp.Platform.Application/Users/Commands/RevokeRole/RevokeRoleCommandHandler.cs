using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Users.Commands.RevokeRole;

/// <summary>
/// AUT-C2 — RevokeRole (docs/requirements.md, "Role Assignment").
///
/// load → validate → domain operation → persist → audit, in the pipeline's
/// transaction. Authorisation (role.revoke, human caller) has already run.
///
/// THE DOMAIN OWNS WHAT REVOCATION MEANS: an active assignment ends at the
/// revocation time, a future one is closed at its own start (an empty period),
/// and an ended or already-revoked one is refused. UserRole.Revoke states the
/// two cases separately; this handler does not choose between them.
/// </summary>
public sealed class RevokeRoleCommandHandler : ICommandHandler<RevokeRoleCommand, RevokeRoleResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IAuditEvents _auditEvents;

    public RevokeRoleCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRoleRepository userRoleRepository,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRoleRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _executionContext = executionContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userRoleRepository = userRoleRepository;
        _auditEvents = auditEvents;
    }

    public async Task<RevokeRoleResult> Handle(RevokeRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new BusinessRuleViolationException("A reason is required to revoke a role.");

        var now = RoleAssignmentTime.AtStoredPrecision(_clock.UtcNow);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var assignment = await _userRoleRepository.FindAsync(command.AssignmentId, ct)
                    ?? throw new BusinessRuleViolationException("The role assignment does not exist.");

                var before = new { effectiveTo = assignment.EffectiveTo };

                try
                {
                    assignment.Revoke(now, _executionContext.UserId, command.Reason);
                }
                catch (DomainException refusal)
                {
                    throw new BusinessRuleViolationException(refusal.Message);
                }

                _auditEvents.Emit("RoleRevoked", version: 1)
                    .Primary("UserRoleAssignment", assignment.Id.Value)
                    .Ref("User", assignment.UserId.Value, role: "Subject")
                    .Ref("Role", assignment.RoleId.Value, role: "RevokedRole")
                    .WithBefore(before)
                    .WithAfter(new
                    {
                        effectiveTo = assignment.EffectiveTo,
                        revokedAt = assignment.RevokedAt,
                    })
                    .WithReason(command.Reason);

                return RevokeRoleResult.Accepted;
            },
            cancellationToken);
    }
}
