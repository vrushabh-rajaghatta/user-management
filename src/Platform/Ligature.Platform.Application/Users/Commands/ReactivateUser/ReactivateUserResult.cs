namespace Ligature.Platform.Application.Users.Commands.ReactivateUser;

/// <summary>
/// Deliberately empty: a reactivation produces nothing the caller needs back.
/// The type exists only because the pipeline is generic over a result.
/// </summary>
public sealed record ReactivateUserResult
{
    public static ReactivateUserResult Accepted { get; } = new();

    private ReactivateUserResult()
    {
    }
}
