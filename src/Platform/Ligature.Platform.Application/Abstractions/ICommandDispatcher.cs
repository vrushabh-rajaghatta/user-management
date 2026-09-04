using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Abstractions;

public interface ICommandDispatcher
{
    Task<TResult> SendAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>;
}