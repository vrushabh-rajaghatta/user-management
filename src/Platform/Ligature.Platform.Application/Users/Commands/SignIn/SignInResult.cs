using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.SignIn;

/// <summary>
/// A failed sign-in is an ordinary outcome, not an exception: credentials were
/// presented, authentication failed, and the system recorded that failure. That
/// distinction is what lets the attempt counter COMMIT — throwing would roll it
/// back and lockout would never engage.
///
/// Carries no reason. "Wrong password", "no such user", "locked" and "inactive"
/// are indistinguishable to the caller, so a reason field here would hand back
/// through the type system exactly what the uniform error message withholds.
/// The handler still knows which case occurred and will emit the corresponding
/// audit event when that capability exists.
/// </summary>
public sealed record SignInResult
{
    private SignInResult(bool succeeded, UserSessionId? sessionId)
    {
        Succeeded = succeeded;
        SessionId = sessionId;
    }

    public bool Succeeded { get; }

    /// <summary>
    /// The authoritative session. No access token: what one should be is
    /// unspecified and is a section 17 escalation (docs/requirements.md).
    /// </summary>
    public UserSessionId? SessionId { get; }

    public static SignInResult Success(UserSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        return new SignInResult(true, sessionId);
    }

    public static SignInResult Failure() => new(false, null);
}
