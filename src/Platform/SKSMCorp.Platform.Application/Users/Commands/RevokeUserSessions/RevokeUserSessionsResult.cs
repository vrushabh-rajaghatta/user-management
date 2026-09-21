namespace SKSMCorp.Platform.Application.Users.Commands.RevokeUserSessions;

/// <summary>
/// Deliberately empty — no count. There is one SessionRevoked per session
/// actually ended, and that is where the number lives.
/// </summary>
public sealed record RevokeUserSessionsResult
{
    public static RevokeUserSessionsResult Done { get; } = new();

    private RevokeUserSessionsResult()
    {
    }
}
