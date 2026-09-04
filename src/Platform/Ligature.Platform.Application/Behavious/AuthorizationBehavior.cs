using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Behaviors;

public sealed class AuthorizationBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;

    public AuthorizationBehavior(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock)
    {
        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _clock = clock;
    }

    public async Task<TResult> Handle(
     TCommand command,
     CancellationToken cancellationToken,
     Func<CancellationToken, Task<TResult>> next)
    {
        if (command is not IAuthorizableCommand<TResult> authorizableCommand)
            return await next(cancellationToken);

        var request = new AuthorizationRequest(
            _executionContext.UserId,
            authorizableCommand.RequiredPermission,
            _clock.UtcNow,
            "Global",
            null);

        var authorized =
            await _authorizationService.IsAllowedAsync(
                request,
                cancellationToken);

        if (!authorized)
        {
            throw new BusinessRuleViolationException(
                "The current actor does not have permission to execute this command.");
        }

        return await next(cancellationToken);
    }
}