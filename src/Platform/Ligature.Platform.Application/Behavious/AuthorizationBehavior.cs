using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Behaviors;

/// <summary>
/// Enforces the command's declared permission, and records the assignment
/// that authorised it.
///
/// Recording happens HERE because here is where the answer is known. The
/// alternative — letting Audit work it out later — re-runs the predicate
/// against role and grant tables that have since changed, and answers a
/// question about the past with today's configuration.
///
/// On refusal nothing is recorded, and that is the correct outcome rather
/// than a gap: the caller's identity is already established, so a refusal
/// carries who attempted the act with no authorising role, exactly as the
/// audit model requires of it (IMPL-05).
/// </summary>
public sealed class AuthorizationBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;

    // The narrow write seam, not IExecutionContextInitializer: this behaviour
    // records authority and must not be able to rewrite who the caller is.
    private readonly IAuthorityInitializer _authorityInitializer;

    private readonly IClock _clock;

    public AuthorizationBehavior(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IAuthorityInitializer authorityInitializer,
        IClock clock)
    {
        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _authorityInitializer = authorityInitializer;
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

        var authorization =
            await _authorizationService.IsAllowedAsync(
                request,
                cancellationToken);

        if (!authorization.IsAllowed)
        {
            throw new BusinessRuleViolationException(
                "The current actor does not have permission to execute this command.");
        }

        // Held for the duration of this command and released afterwards,
        // including when the handler throws: the next command in this scope
        // must not inherit it.
        using var authority =
            _authorityInitializer.EstablishAuthority(authorization.Authority!);

        return await next(cancellationToken);
    }
}