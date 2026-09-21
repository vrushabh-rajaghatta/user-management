namespace SKSMCorp.Platform.Application.Users.Commands.AdminResetPassword;

/// <summary>
/// Deliberately empty.
///
/// The one thing this command produces that a caller might want is the token,
/// and the administrator must never receive it — the notification is the only
/// delivery mechanism. A result with a field is a result someone will
/// eventually populate, so there is none. The type exists only because the
/// pipeline is generic over a result.
/// </summary>
public sealed record AdminResetPasswordResult
{
    public static AdminResetPasswordResult Accepted { get; } = new();

    private AdminResetPasswordResult()
    {
    }
}
