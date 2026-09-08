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

        // A command cannot both run without a caller and require that caller to
        // be human. Without this guard the ActorType read below would throw
        // InvalidOperationException from an unestablished context — a confusing
        // infrastructure error for what is a configuration mistake (CRD-C1).
        if (command is IAnonymousCommand<TResult>)
        {
            throw new InvalidOperationException(
                $"'{typeof(TCommand).Name}' declares both IAnonymousCommand and "
                + "IHumanActorOnlyCommand. An anonymous command has no actor "
                + "whose type could be checked.");
        }

        if (_executionContext.ActorType != ActorType.Human)
        {
            throw new BusinessRuleViolationException(
                "Only a human actor can execute this command.");
        }

        return next(cancellationToken);
    }
}