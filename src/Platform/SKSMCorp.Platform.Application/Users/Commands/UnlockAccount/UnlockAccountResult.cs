namespace SKSMCorp.Platform.Application.Users.Commands.UnlockAccount;

/// <summary>
/// Deliberately empty: the before and after state is the audit record's to
/// carry. The type exists only because the pipeline is generic over a result.
/// </summary>
public sealed record UnlockAccountResult
{
    public static UnlockAccountResult Unlocked { get; } = new();

    private UnlockAccountResult()
    {
    }
}
