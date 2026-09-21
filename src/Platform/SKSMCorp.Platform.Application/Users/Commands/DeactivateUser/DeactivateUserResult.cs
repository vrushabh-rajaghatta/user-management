namespace SKSMCorp.Platform.Application.Users.Commands.DeactivateUser;

/// <summary>
/// Deliberately empty: a deactivation produces nothing the caller needs back.
/// The type exists only because the pipeline is generic over a result.
/// </summary>
public sealed record DeactivateUserResult
{
    public static DeactivateUserResult Accepted { get; } = new();

    private DeactivateUserResult()
    {
    }
}
