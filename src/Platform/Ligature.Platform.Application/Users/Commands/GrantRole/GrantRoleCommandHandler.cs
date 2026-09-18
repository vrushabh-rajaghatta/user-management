using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.GrantRole;

/// <summary>AUT-C1. RED STUB, and not registered.</summary>
public sealed class GrantRoleCommandHandler : ICommandHandler<GrantRoleCommand, GrantRoleResult>
{
    public Task<GrantRoleResult> Handle(GrantRoleCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-C1 is not implemented.");
}
