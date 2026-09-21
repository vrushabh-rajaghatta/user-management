using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users;

/// <summary>
/// What SES-C3 and both SES-C4 commands share: the controlled revocation codes
/// they write, and the one shape of the SessionRevoked record each of them
/// declares.
///
/// TWO FIELDS, TWO MEANINGS (D2, R1). user_session.RevocationReason is a
/// controlled CODE naming the category of termination — AdminRevoked,
/// SignOutEverywhere. The audit record's Reason is the human EXPLANATION the
/// command supplied. The code still reaches the audit trail, through the
/// session's Before/After representation, so SessionRevoked keeps its
/// BeforeAfter shape and gains no payload.
///
/// The vocabulary is normative but not enforced by the database: the
/// revocation_reason column has no CHECK (docs/requirements.md). These
/// constants are where the application holds it.
///
/// Declaring only. The pipeline resolves the catalogue, captures the snapshot,
/// validates and writes the record in the command's transaction — nothing here
/// builds an envelope.
/// </summary>
internal static class SessionRevocations
{
    /// <summary>SES-C3 RevokeSession and SES-C4 RevokeUserSessions.</summary>
    internal const string AdminRevoked = "AdminRevoked";

    /// <summary>SES-C4 SignOutEverywhere.</summary>
    internal const string SignOutEverywhere = "SignOutEverywhere";

    /// <summary>USR-C4 DeactivateUser's cascade (D4).</summary>
    internal const string UserDeactivated = "UserDeactivated";

    /// <summary>
    /// CRD-C4's attempt limit (L5): this session made the effective
    /// MaxFailedLoginAttempts consecutive failed current-password attempts.
    /// </summary>
    internal const string PasswordChangeAttemptsExceeded = "PasswordChangeAttemptsExceeded";

    /// <summary>
    /// Records a revocation that has already happened on <paramref name="session"/>
    /// — call only when UserSession.Revoke returned true, so a record never
    /// describes a termination that did not occur.
    /// </summary>
    internal static void Declare(
        IAuditEvents auditEvents,
        UserSession session,
        UserId owner,
        string explanation,
        AuditEventDeclaration? cause = null)
    {
        ArgumentNullException.ThrowIfNull(auditEvents);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(owner);

        if (session.RevokedAt is null || session.RevokedBy is null || session.RevocationReason is null)
        {
            throw new InvalidOperationException(
                "Session revocation defect — SessionRevoked was declared for a "
                + "session that has not been revoked.");
        }

        var declaration = auditEvents.Emit("SessionRevoked", version: 1)
            .Primary("Session", session.Id.Value)
            .Ref("Identity", session.UserIdentityId.Value, role: "Target")
            .Ref("User", owner.Value, role: "Subject")
            .WithBefore(new
            {
                RevokedAt = (DateTimeOffset?)null,
                RevokedBy = (Guid?)null,
                RevocationReason = (string?)null,
            })
            .WithAfter(new
            {
                RevokedAt = session.RevokedAt,
                RevokedBy = (Guid?)session.RevokedBy.Value,
                RevocationReason = session.RevocationReason,
            })
            .WithReason(explanation);

        // A cascade step names the event that started it (behaviour 18).
        if (cause is not null)
            declaration.CausedBy(cause);
    }
}
