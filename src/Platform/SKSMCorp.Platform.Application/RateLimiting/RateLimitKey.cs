namespace SKSMCorp.Platform.Application.RateLimiting;

/// <summary>A normalised key under one rule: one bucket.</summary>
public sealed record RateLimitKey(RateLimitRule Rule, string Value);
