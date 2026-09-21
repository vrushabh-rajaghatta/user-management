using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Behaviors;

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
    ///
    /// A bearer-authenticated identity-establishing command may execute only
    /// when no caller is already established in the execution context
    /// (<see cref="IBearerAuthenticatedCommand{TResult}"/>). The marker is the
    /// whole test: no command type is named here.
    ///
    /// The refusal belongs in this behaviour rather than in the handlers, and
    /// the position is load-bearing. It runs before the transaction opens. An
    /// execution strategy retries the transaction — and with it the handler —
    /// in the same scope, where the first attempt has already established the
    /// bearer, so a check inside the retried work would refuse its own retry.
    /// This decides whether the command may START; BearerActorEstablisher
    /// tolerates the replay of one that did.
    /// </summary>
    public Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        if (command is IBearerAuthenticatedCommand<TResult>)
        {
            // Before anything is verified, consumed or declared — so the answer
            // cannot depend on whether the credential it carries is right.
            if (_executionContext.IsAuthenticated)
            {
                throw new AuthenticationFailedException(
                    "A bearer-authenticated command cannot start under an established caller.");
            }

            return next(cancellationToken);
        }

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