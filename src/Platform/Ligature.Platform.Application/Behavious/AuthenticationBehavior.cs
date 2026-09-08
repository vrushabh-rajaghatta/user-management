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

    /// <summary>
    /// Fails closed: a command is authenticated unless it declares otherwise
    /// (CRD-C1). See <see cref="IAnonymousCommand{TResult}"/> for why the
    /// marker means "anonymous" rather than "authenticated".
    /// </summary>
    public Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        if (command is IAnonymousCommand<TResult>)
            return next(cancellationToken);

        if (!_executionContext.IsAuthenticated)
        {
            throw new AuthenticationFailedException(
                "An authenticated user is required.");
        }

        return next(cancellationToken);
    }
}