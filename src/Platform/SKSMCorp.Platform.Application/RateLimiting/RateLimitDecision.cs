namespace SKSMCorp.Platform.Application.RateLimiting;

/// <summary>
/// The store's answer. When refused, <paramref name="RetryAfter"/> is the time
/// until every exhausted bucket has room again, and <paramref name="Exhausted"/>
/// names their kinds. When admitted, both are empty.
/// </summary>
public sealed record RateLimitDecision(
    bool Admitted,
    TimeSpan RetryAfter,
    IReadOnlyList<RateLimitKeyKind> Exhausted)
{
    public static readonly RateLimitDecision Admit =
        new(true, TimeSpan.Zero, Array.Empty<RateLimitKeyKind>());
}
