namespace Ligature.Platform.Application.Users.Commands.SignOut;

/// <summary>
/// Deliberately empty.
///
/// A field saying whether this call was the one that revoked the session would
/// distinguish "already signed out" from "that session is not yours" from "no
/// such session" — precisely the three states SES-C2 collapses so that a
/// SessionId cannot be probed for existence. The contract is only that the
/// caller asked to sign themselves out and the command completed.
/// </summary>
public sealed record SignOutResult;
