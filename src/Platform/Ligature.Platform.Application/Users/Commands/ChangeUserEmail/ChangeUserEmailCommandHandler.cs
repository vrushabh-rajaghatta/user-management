using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.ChangeUserEmail;

// Compile-only stub for the red tests of "USR-C3 ChangeUserEmail"
// (docs/requirements.md). Not yet implemented: it changes nothing.
public sealed class ChangeUserEmailCommandHandler : ICommandHandler<ChangeUserEmailCommand, ChangeUserEmailResult>
{
    public Task<ChangeUserEmailResult> Handle(ChangeUserEmailCommand command, CancellationToken cancellationToken)
        => Task.FromResult(ChangeUserEmailResult.Accepted);
}
