namespace Ligature.SharedKernel.Abstractions;

public interface ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next);
}