namespace Ligature.Platform.Application.Users.Commands.ChangePassword;

/// <summary>
/// What CRD-C4 decided. Counted refusals are OUTCOMES rather than exceptions
/// (docs/requirements.md, "CRD-C4 — limiting current-password attempts per
/// session"): a thrown refusal rolls back, and the session's count must commit
/// although the command refuses.
/// </summary>
public enum ChangePasswordOutcome
{
    /// <summary>The password changed. 204.</summary>
    Changed,

    /// <summary>A counted refusal below the threshold. The uniform 400.</summary>
    Refused,

    /// <summary>This session is over: ended by the limit now, or before this request acted. 401.</summary>
    SessionEnded,
}

/// <summary>
/// Deliberately carries no detail beyond the outcome. Whether other sessions
/// were revoked is recorded in the audit trail; the response has nothing to
/// add, and a count here would be a statement about the caller's other devices
/// handed to whoever holds this session. Nor is the attempt count exposed.
/// </summary>
public sealed record ChangePasswordResult
{
    /// <summary>
    /// The one refusal message CRD-C4 gives for everything it will not explain:
    /// a wrong current password, a locked credential, and a session it cannot
    /// act for. The wording is not a frozen contract; its uniformity is.
    /// </summary>
    public const string NotChanged = "The password could not be changed.";

    public static ChangePasswordResult Changed { get; } = new(ChangePasswordOutcome.Changed);

    public static ChangePasswordResult Refused { get; } = new(ChangePasswordOutcome.Refused);

    public static ChangePasswordResult SessionEnded { get; } = new(ChangePasswordOutcome.SessionEnded);

    private ChangePasswordResult(ChangePasswordOutcome outcome) => Outcome = outcome;

    public ChangePasswordOutcome Outcome { get; }
}
