using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.RevokeRole;

/// <summary>AUT-C2. RED STUB, and not registered.</summary>
public sealed class RevokeRoleCommandHandler : ICommandHandler<RevokeRoleCommand, RevokeRoleResult>
{
    public Task<RevokeRoleResult> Handle(RevokeRoleCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-C2 is not implemented.");
}
