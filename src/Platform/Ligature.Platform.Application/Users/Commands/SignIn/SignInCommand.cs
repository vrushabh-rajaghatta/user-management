using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.RateLimiting;

namespace Ligature.Platform.Application.Users.Commands.SignIn;

/// <summary>
/// SES-C1. Anonymous by definition — sign-in is what produces an authenticated
/// caller, so it cannot require one. Bearer-authenticated as well: the password
/// is its credential, and the caller it establishes is that password's owner, so
/// it may not start under a caller who is already established.
/// </summary>
/// <param name="Username">
/// The LOCAL identity's username. Not an email address: the catalogue names
/// this input "UsernameOrEmail" because it also covers external assertions, but
/// local authentication resolves by username alone, and email must never become
/// an identity lookup key.
/// </param>
public sealed record SignInCommand(
    string Username,
    string Password,
    string? IpAddress,
    string? UserAgent)
    : IBearerAuthenticatedCommand<SignInResult>, IRateLimitedCommand
{
    /// <summary>Behaviour 11: per username and per client address.</summary>
    IReadOnlyList<RateLimitSubject> IRateLimitedCommand.RateLimitSubjects =>
    [
        new(RateLimitRules.SignInByUsername, Username),
        new(RateLimitRules.SignInByClientAddress, IpAddress),
    ];
}
