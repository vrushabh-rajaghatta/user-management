using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Dispatching;

public sealed class CommandPipeline
{
    public Task<TResult> ExecuteAsync<TCommand, TResult>(
        TCommand command,
        IReadOnlyList<ICommandBehavior<TCommand, TResult>> behaviors,
        ICommandHandler<TCommand, TResult> handler,
        CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        Func<CancellationToken, Task<TResult>> next =
            ct => handler.Handle(command, ct);

        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var currentNext = next;

            next = ct =>
                behavior.Handle(
                    command,
                    ct,
                    currentNext);
        }

        return next(cancellationToken);
    }
}