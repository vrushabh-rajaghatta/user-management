namespace Ligature.Platform.Application.Users.Commands.ChangeUserEmail;

/// <summary>
/// Deliberately empty: a change and a no-op are both 204 (CE6), and nothing
/// comes back that the caller needs.
/// </summary>
public sealed record ChangeUserEmailResult
{
    public static ChangeUserEmailResult Accepted { get; } = new();

    private ChangeUserEmailResult()
    {
    }
}
