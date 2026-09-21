namespace SKSMCorp.Platform.Application.Users.Commands.ReissueActivationLink;

/// <summary>
/// Deliberately empty, for AdminResetPasswordResult's reason: the one thing
/// this command produces that a caller might want is the token, and the
/// administrator must never receive it. The type exists only because the
/// pipeline is generic over a result.
/// </summary>
public sealed record ReissueActivationLinkResult
{
    public static ReissueActivationLinkResult Accepted { get; } = new();

    private ReissueActivationLinkResult()
    {
    }
}
