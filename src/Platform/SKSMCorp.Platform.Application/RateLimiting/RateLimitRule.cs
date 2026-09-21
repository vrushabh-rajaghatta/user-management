namespace SKSMCorp.Platform.Application.RateLimiting;

/// <summary>
/// One bucket family: at most <paramref name="Limit"/> admitted requests per
/// key within any <paramref name="Window"/>. The <paramref name="Name"/> keeps
/// one command's buckets apart from another's on the same key.
/// </summary>
public sealed record RateLimitRule(
    string Name,
    RateLimitKeyKind Kind,
    int Limit,
    TimeSpan Window);
