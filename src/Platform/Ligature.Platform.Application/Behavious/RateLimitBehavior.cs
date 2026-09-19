using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.RateLimiting;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Behaviors;

/// <summary>
/// Behaviour 11 (docs/requirements.md, "Behaviour 11 — rate limiting the
/// anonymous commands").
///
/// FIRST IN THE PIPELINE, and the position is the point: a refused request
/// reaches no later behaviour — not authentication, not the transaction, not
/// the handler — so it costs no lookup and no password derivation, and it
/// changes no authentication state. A 429 means abuse protection fired, not
/// that authentication failed: nothing here touches FailedAttemptCount or
/// LockedUntil, and nothing is audited (B7).
///
/// It measures REQUEST VOLUME. Whatever the command later decides — success,
/// wrong password, no such account — the admitted request has already counted.
///
/// The behaviour tests the marker and names no command. It normalises each
/// declared value by its rule's kind, so a command cannot forget to, and reads
/// the clock once.
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

    public Task<TResult> Handle(
        TCommand command,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> next)
    {
        if (command is not IRateLimitedCommand limited)
            return next(cancellationToken);

        var keys = new List<RateLimitKey>();
        string? clientAddress = null;

        foreach (var subject in limited.RateLimitSubjects)
        {
            if (subject.Rule.Kind == RateLimitKeyKind.ClientAddress)
                clientAddress ??= subject.Value;

            // No key, no gate for that kind — never a shared "unknown" bucket.
            var value = RateLimitKeys.Normalize(subject.Rule.Kind, subject.Value);

            if (value is not null)
                keys.Add(new RateLimitKey(subject.Rule, value));
        }

        var decision = _store.TryAdmit(keys, _clock.UtcNow);

        if (!decision.Admitted)
        {
            // What the operational warning needs, and nothing it must not
            // carry: the typed username or address stays here.
            throw new RateLimitExceededException(
                decision.RetryAfter,
                decision.Exhausted.Select(KindName).ToList(),
                clientAddress);
        }

        return next(cancellationToken);
    }

    private static string KindName(RateLimitKeyKind kind)
        => kind == RateLimitKeyKind.ClientAddress ? "ip" : "address";
}
