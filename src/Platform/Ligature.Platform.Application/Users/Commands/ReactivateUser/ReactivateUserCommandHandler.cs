using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.ReactivateUser;

/// <summary>USR-C5. Stub — not yet implemented.</summary>
public sealed class ReactivateUserCommandHandler : ICommandHandler<ReactivateUserCommand, ReactivateUserResult>
{
    public ReactivateUserCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IAuditEvents auditEvents)
    {
    }

    public Task<ReactivateUserResult> Handle(ReactivateUserCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
