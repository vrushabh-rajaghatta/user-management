using SKSMCorp.Platform.Application.RateLimiting;

namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// Declares the rate limits a command is admitted under (behaviour 11). Every
/// IAnonymousCommand must declare them; a test holds the two together.
///
/// Like IAnonymousCommand, it states a fact about the command. What to do with
/// it belongs to RateLimitBehavior, which names no command.
/// </summary>
public interface IRateLimitedCommand
{
    IReadOnlyList<RateLimitSubject> RateLimitSubjects { get; }
}
