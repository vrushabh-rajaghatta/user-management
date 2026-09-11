namespace Ligature.Platform.Application.Users.Commands.RequestPasswordReset;

/// <summary>
/// Deliberately empty, and that is the whole design.
///
/// CRD-C2 must return an identical answer whether or not the account exists,
/// so there is nothing for this to carry: no id, no flag, no count, not even
/// a nullable that happens to be null on one branch. A result type with a
/// field is a result type someone will populate on the branch where a value
/// is available, and that is the account-enumeration oracle the command's
/// frozen failure mode exists to prevent.
///
/// The type exists only because the pipeline is generic over a result. A
/// single instance, so two requests cannot even differ by reference.
/// </summary>
public sealed record RequestPasswordResetResult
{
    public static RequestPasswordResetResult Accepted { get; } = new();

    private RequestPasswordResetResult()
    {
    }
}
