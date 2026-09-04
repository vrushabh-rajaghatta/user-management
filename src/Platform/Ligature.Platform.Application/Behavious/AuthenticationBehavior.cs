using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Behaviors;

public sealed class AuthenticationBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly IExecutionContext _executionContext;

    public AuthenticationBehavior(
        IExecutionContext executionContext)
    {
        _executionContext = executionContext;
    }

    public Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        if (!_executionContext.IsAuthenticated)
        {
            throw new AuthenticationFailedException(
                "An authenticated user is required.");
        }

        return next(cancellationToken);
    }
}