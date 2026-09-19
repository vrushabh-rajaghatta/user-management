namespace Ligature.Platform.Application.RateLimiting;

/// <summary>What a bucket is keyed on. Each has its own normalisation.</summary>
public enum RateLimitKeyKind
{
    /// <summary>The client address the Host resolved (B8).</summary>
    ClientAddress,

    /// <summary>The typed username, email or both, normalised (B3).</summary>
    Address,
}
