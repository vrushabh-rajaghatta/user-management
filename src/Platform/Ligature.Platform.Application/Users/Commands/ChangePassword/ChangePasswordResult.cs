namespace Ligature.Platform.Application.Users.Commands.ChangePassword;

/// <summary>
/// Deliberately empty. Whether other sessions were revoked is recorded in the
/// audit trail; the response has nothing to add, and a count here would be a
/// statement about the caller's other devices handed to whoever holds this
/// session.
/// </summary>
public sealed record ChangePasswordResult
{
    public static ChangePasswordResult Changed { get; } = new();

    private ChangePasswordResult()
    {
    }
}
