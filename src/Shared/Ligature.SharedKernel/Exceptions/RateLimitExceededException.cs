namespace Ligature.SharedKernel.Exceptions;

/// <summary>
/// Behaviour 11 refused a request: an applicable rate-limit bucket had no room
/// (docs/requirements.md, "Behaviour 11 — rate limiting the anonymous
/// commands"). Not an authentication failure — the command never started.
///
/// The Host maps it to 429 with a fixed sentence and reads nothing off it but
/// <see cref="RetryAfter"/> for the response. <see cref="ExhaustedKeyKinds"/>
/// and <see cref="ClientAddress"/> are for the operational warning only, and
/// the typed username or address is deliberately not carried at all.
/// </summary>
public sealed class RateLimitExceededException : Exception
{
    public RateLimitExceededException(
        TimeSpan retryAfter,
        IReadOnlyList<string> exhaustedKeyKinds,
        string? clientAddress)
        : base("The request was refused by rate limiting.")
    {
        RetryAfter = retryAfter;
        ExhaustedKeyKinds = exhaustedKeyKinds;
        ClientAddress = clientAddress;
    }

    /// <summary>Until every exhausted bucket for the request has room again.</summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>"ip" and/or "address".</summary>
    public IReadOnlyList<string> ExhaustedKeyKinds { get; }

    /// <summary>The resolved client address, or null when none was resolvable.</summary>
    public string? ClientAddress { get; }
}
