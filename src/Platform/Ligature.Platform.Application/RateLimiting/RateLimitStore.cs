namespace Ligature.Platform.Application.RateLimiting;

/// <summary>
/// The in-memory buckets (B10). A singleton: one per host process.
/// </summary>
public sealed class RateLimitStore
{
    // RED-TEST STUB: admits everything and tracks nothing.
    public RateLimitDecision TryAdmit(IReadOnlyList<RateLimitKey> keys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return RateLimitDecision.Admit;
    }

    /// <summary>How many keys the store currently holds.</summary>
    public int TrackedKeyCount => 0;
}
