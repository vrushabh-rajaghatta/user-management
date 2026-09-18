using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.UpdateUserProfile;

/// <summary>USR-C2. Stub — not yet implemented.</summary>
public sealed class UpdateUserProfileCommandHandler : ICommandHandler<UpdateUserProfileCommand, UpdateUserProfileResult>
{
    public UpdateUserProfileCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository, IAuditEvents auditEvents)
    {
    }

    public Task<UpdateUserProfileResult> Handle(UpdateUserProfileCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
