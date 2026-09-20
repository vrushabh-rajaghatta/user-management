using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Commands.CreateRole;

// Compile-only stub for the red tests of "AUT-C3 CreateRole"
// (docs/requirements.md). Not yet implemented: it creates nothing.
public sealed class CreateRoleCommandHandler : ICommandHandler<CreateRoleCommand, CreateRoleResult>
{
    public Task<CreateRoleResult> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
        => Task.FromResult(new CreateRoleResult(RoleId.New(), command.Code, command.Name, command.Description, false, true));
}
