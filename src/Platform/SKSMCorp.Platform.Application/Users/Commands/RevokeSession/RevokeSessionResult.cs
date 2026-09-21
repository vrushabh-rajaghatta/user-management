namespace SKSMCorp.Platform.Application.Users.Commands.RevokeSession;

/// <summary>
/// Deliberately empty: whether a revocation happened is the audit trail's to
/// say, and an already-ended session is a no-op the response does not
/// distinguish.
/// </summary>
public sealed record RevokeSessionResult
{
    public static RevokeSessionResult Done { get; } = new();

    private RevokeSessionResult()
    {
    }
}
