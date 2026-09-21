using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Abstractions;

public interface ICommandDispatcher
{
    Task<TResult> SendAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>;
}