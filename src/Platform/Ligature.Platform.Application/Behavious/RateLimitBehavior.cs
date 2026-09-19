using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Ligature.Platform.Application.RateLimiting;

namespace Ligature.Platform.Application.Behaviors;

/// <summary>
/// Behaviour 11 (docs/requirements.md, "Behaviour 11 — rate limiting the
/// anonymous commands").
/// </summary>
public sealed class RateLimitBehavior<TCommand, TResult>
    : ICommandBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly RateLimitStore _store;
    private readonly IClock _clock;

    public RateLimitBehavior(RateLimitStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    // RED-TEST STUB: passes every command through.
    public Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
        => next(cancellationToken);
}
