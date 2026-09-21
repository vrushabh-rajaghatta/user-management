namespace SKSMCorp.Platform.Application.RateLimiting;

/// <summary>
/// A rule and the RAW value the command carries for it. The behaviour
/// normalises the value by the rule's kind, so a command cannot forget to.
/// </summary>
public sealed record RateLimitSubject(RateLimitRule Rule, string? Value);
