namespace Ligature.Platform.Application.Users.Commands.ChangePassword;

/// <summary>
/// COMPILE-ONLY STUB (red tests). The outcome CRD-C4's counted refusals return
/// instead of throwing, so the per-session counter commits.
/// </summary>
public enum ChangePasswordOutcome
{
    Changed,
    Refused,
    SessionEnded,
}

/// <summary>
/// Deliberately carries no detail. Whether other sessions were revoked is
/// recorded in the audit trail; the response has nothing to add, and a count
/// here would be a statement about the caller's other devices handed to
/// whoever holds this session.
/// </summary>
public sealed record ChangePasswordResult
{
    /// <summary>The one refusal message CRD-C4 gives for everything it will not explain.</summary>
    public const string NotChanged = "The password could not be changed.";

    public static ChangePasswordResult Changed { get; } = new(ChangePasswordOutcome.Changed);

    public static ChangePasswordResult Refused { get; } = new(ChangePasswordOutcome.Refused);

    public static ChangePasswordResult SessionEnded { get; } = new(ChangePasswordOutcome.SessionEnded);

    private ChangePasswordResult(ChangePasswordOutcome outcome) => Outcome = outcome;

    public ChangePasswordOutcome Outcome { get; }
}
