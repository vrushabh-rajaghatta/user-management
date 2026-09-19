namespace Ligature.Platform.Application.RateLimiting;

/// <summary>
/// How a raw value becomes a bucket key. Null means "no key": the gate for
/// that kind does not apply to the request.
/// </summary>
public static class RateLimitKeys
{
    public static string? Normalize(RateLimitKeyKind kind, string? value)
        => kind switch
        {
            RateLimitKeyKind.Address => NormalizeAddress(value),
            RateLimitKeyKind.ClientAddress => NormalizeClientAddress(value),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    // RED-TEST STUB: returns the value unchanged.
    public static string? NormalizeAddress(string? value) => value;

    // RED-TEST STUB: returns the value unchanged.
    public static string? NormalizeClientAddress(string? value) => value;
}
