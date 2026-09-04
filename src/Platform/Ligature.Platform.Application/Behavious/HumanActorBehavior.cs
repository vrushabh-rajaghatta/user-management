using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Behaviors;

public sealed class HumanActorBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly IExecutionContext _executionContext;

    public HumanActorBehavior(
        IExecutionContext executionContext)
    {
        _executionContext = executionContext;
    }

    public Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        if (command is not IHumanActorOnlyCommand<TResult>)
            return next(cancellationToken);

        if (_executionContext.ActorType != ActorType.Human)
        {
            throw new BusinessRuleViolationException(
                "Only a human actor can execute this command.");
        }

        return next(cancellationToken);
    }
}