using Ligature.Platform.Application.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.SignIn;

/// <summary>
/// SES-C1. Anonymous by definition — sign-in is what produces an authenticated
/// caller, so it cannot require one. Reuses the marker CRD-C1 introduced rather
/// than adding a second authentication path.
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
    : IAnonymousCommand<SignInResult>;
