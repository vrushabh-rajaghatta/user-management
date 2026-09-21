namespace SKSMCorp.Platform.Application.Users.Commands.SignOutEverywhere;

/// <summary>
/// Deliberately empty — no count and no list of the devices that were signed
/// out. The audit trail holds one SessionRevoked per session ended.
/// </summary>
public sealed record SignOutEverywhereResult
{
    public static SignOutEverywhereResult Done { get; } = new();

    private SignOutEverywhereResult()
    {
    }
}
