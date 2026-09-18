using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.DeactivateUser;

/// <summary>USR-C4. Stub — not yet implemented.</summary>
public sealed class DeactivateUserCommandHandler : ICommandHandler<DeactivateUserCommand, DeactivateUserResult>
{
    public DeactivateUserCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IUserRoleRepository userRoleRepository,
        IUserSessionRepository userSessionRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IUserTokenRepository userTokenRepository,
        IAuditEvents auditEvents)
    {
    }

    public Task<DeactivateUserResult> Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
