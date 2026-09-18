namespace Ligature.Platform.Application.Users.Commands.RevokeRole;

/// <summary>
/// Deliberately empty: a revocation produces nothing the caller needs back.
/// The type exists only because the pipeline is generic over a result.
/// </summary>
public sealed record RevokeRoleResult
{
    public static RevokeRoleResult Accepted { get; } = new();

    private RevokeRoleResult()
    {
    }
}
