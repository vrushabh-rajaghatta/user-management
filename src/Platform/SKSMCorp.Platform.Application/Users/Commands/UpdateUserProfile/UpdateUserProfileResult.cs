namespace SKSMCorp.Platform.Application.Users.Commands.UpdateUserProfile;

/// <summary>
/// Deliberately empty: an update produces nothing the caller needs back, and
/// a no-op is not distinguished from a change (both are 204).
/// </summary>
public sealed record UpdateUserProfileResult
{
    public static UpdateUserProfileResult Accepted { get; } = new();

    private UpdateUserProfileResult()
    {
    }
}
