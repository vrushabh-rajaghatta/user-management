using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.AdminResetPassword;
using Ligature.Platform.Application.Users.Commands.ChangePassword;
using Ligature.Platform.Application.Users.Commands.GrantRole;
using Ligature.Platform.Application.Users.Commands.DeactivateUser;
using Ligature.Platform.Application.Users.Commands.ReactivateUser;
using Ligature.Platform.Application.Users.Commands.RevokeRole;
using Ligature.Platform.Application.Users.Commands.UpdateUserProfile;
using Ligature.Platform.Application.Users.Commands.ReissueActivationLink;
using Ligature.Platform.Application.Users.Commands.ResetPassword;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Commands.RequestPasswordReset;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Application.Users.Commands.SignOut;
using Ligature.Platform.Application.Users.Commands.RevokeSession;
using Ligature.Platform.Application.Users.Commands.RevokeUserSessions;
using Ligature.Platform.Application.Users.Commands.SignOutEverywhere;
using Ligature.Platform.Application.Users.Commands.UnlockAccount;

namespace Ligature.Platform.Application.Audit;

/// <summary>
/// Which commands emit which events — the static half of IMPL-08.
///
/// The catalogue is data and a handler's declarations are runtime calls, so
/// "every code a compiled handler declares exists and is active" cannot be
/// checked at start unless the declarations are also written down
/// statically. They are written down here, beside the handlers' own explicit
/// registration and in the same spirit: "which commands emit what" is a
/// question a reviewer answers by reading this class, not by reasoning about
/// what a handler might call.
///
/// Two checks depend on it. The host verifies at start that every code
/// listed here exists in the catalogue and is active, and refuses to run
/// otherwise — a deployment failure before any command runs, rather than a
/// runtime failure on the first affected command. And behaviour 7 refuses,
/// as a defect, an emission of a code the command did not list, so the list
/// cannot silently fall out of date.
///
/// A context may emit only codes it owns (emission contract). Every V1
/// command is User Management's; the context is recorded per entry so that
/// stops being an assumption the day a second context emits.
/// </summary>
public static class AuditDeclarations
{
    private static readonly IReadOnlyDictionary<Type, AuditDeclaration> ByCommand =
        new Dictionary<Type, AuditDeclaration>
        {
            // USR-C1 — one record per created row. TokenInvalidated is not
            // listed: USR-C1 issues the first token a brand-new identity has,
            // so there is never a prior one to supersede. CRD-C2 below is
            // where UT5 actually bites.
            [typeof(CreateUserCommand)] = new(
                "UserManagement",
                ["UserCreated", "IdentityCreated", "TokenIssued"]),

            // CRD-C2 — both codes only on the known-account branch, and both
            // attributed to System: nobody authenticated, and the catalogue
            // permits only a System origin for PasswordResetRequested. The
            // silent branch declares NOTHING, which is why this list has no
            // entry for a rejected or unmatched request — a record of one
            // would be the enumeration oracle the command exists to avoid.
            //
            // TokenInvalidated (n), one per superseded token (UT5).
            [typeof(RequestPasswordResetCommand)] = new(
                "UserManagement",
                ["PasswordResetRequested", "TokenInvalidated"]),

            // CRD-C3 — the bearer's two on success, both Authenticated because
            // the consumed token establishes the actor (AUD-D28), exactly as
            // CRD-C1. A refused token emits TokenRejected, autonomous and
            // anonymous, so it survives the rollback. A password refused after
            // the token proved itself emits nothing: the transaction rolls
            // back and the token is still usable.
            [typeof(ResetPasswordCommand)] = new(
                "UserManagement",
                ["TokenConsumed", "PasswordReset", "TokenRejected"]),

            // CRD-C5 — all three as the ADMINISTRATOR, Authenticated. Unlike
            // CRD-C2 nothing here is AsSystem: a person holding
            // user.resetpassword authorised this, and the record must say who.
            // TokenInvalidated (n), one per superseded token (UT5); only
            // AdminPasswordResetIssued requires the reason. A refused target
            // declares nothing — every eligibility check precedes every write.
            [typeof(AdminResetPasswordCommand)] = new(
                "UserManagement",
                ["AdminPasswordResetIssued", "TokenIssued", "TokenInvalidated"]),

            // CRD-C7 — existing events only, as the ADMINISTRATOR: the new
            // token, carrying the required reason, and TokenInvalidated (n),
            // one per superseded token (UT5). No reissue event: these two
            // describe it. A refused target declares nothing.
            [typeof(ReissueActivationLinkCommand)] = new(
                "UserManagement",
                ["TokenIssued", "TokenInvalidated"]),

            // AUT-C1 / AUT-C2 — one record each, as the ADMINISTRATOR, each
            // carrying the required reason. A refusal declares nothing.
            [typeof(GrantRoleCommand)] = new("UserManagement", ["RoleGranted"]),
            [typeof(RevokeRoleCommand)] = new("UserManagement", ["RoleRevoked"]),

            // USR-C2 — one record, and only when a name actually changed.
            [typeof(UpdateUserProfileCommand)] = new("UserManagement", ["UserProfileChanged"]),

            // USR-C4 — the cascade, one operation caused by UserDeactivated.
            // NOT TokenInvalidated: its frozen definition requires a
            // SupersededBy token and deactivation issues none (D13).
            [typeof(DeactivateUserCommand)] = new(
                "UserManagement",
                ["UserDeactivated", "IdentityDeactivated", "RoleRevoked", "SessionRevoked"]),

            // USR-C5 — restores nothing, so only the lifecycle events.
            [typeof(ReactivateUserCommand)] = new(
                "UserManagement",
                ["UserReactivated", "IdentityReactivated"]),

            // SES-C3 and both SES-C4 commands — SessionRevoked (n), one per
            // session actually ended, each with the caller's explanation as its
            // Reason and the controlled code in its After (D2). An unknown
            // target is refused and an already-ended session or an empty set
            // is a no-op: neither declares anything.
            [typeof(RevokeSessionCommand)] = new(
                "UserManagement",
                ["SessionRevoked"]),

            [typeof(RevokeUserSessionsCommand)] = new(
                "UserManagement",
                ["SessionRevoked"]),

            [typeof(SignOutEverywhereCommand)] = new(
                "UserManagement",
                ["SessionRevoked"]),

            // CRD-C6 — one record, and only when a live lock was actually
            // cleared. Every refusal (ineligible, not locked, own account)
            // precedes the write and declares nothing.
            [typeof(UnlockAccountCommand)] = new(
                "UserManagement",
                ["AccountUnlocked"]),

            // CRD-C4 — PasswordChanged, and SessionRevoked (n) for each other
            // session of the identity that A5 ends, each caused by the change.
            //
            // SessionRevoked here is a CATALOGUE AMENDMENT, not an existing
            // producer: the frozen Audit Event Catalogue does not list CRD-C4
            // among its producers, and the UM command catalogue lists only
            // PasswordChanged for CRD-C4. Resolving A5 as (b) is what requires
            // it; see docs/requirements.md. A refused change declares nothing.
            [typeof(ChangePasswordCommand)] = new(
                "UserManagement",
                ["PasswordChanged", "SessionRevoked"]),

            // SES-C2 — one record, and only when a session actually changed
            // state. The no-op outcomes declare nothing: a record of a
            // revocation that did not happen would be false, and the refusal
            // of an attempt against someone else's session is AuthorisationDenied,
            // which is autonomous and arrives with E2b.
            [typeof(SignOutCommand)] = new(
                "UserManagement",
                ["SignedOut"]),

            // CRD-C1 — the bearer's own three. All require an authenticated
            // origin, which is why this command establishes its actor from
            // the identity its token consumption returned (AUD-D28). A
            // rejected token emits TokenRejected, which is autonomous and
            // arrives with E2b.
            [typeof(ActivateAccountCommand)] = new(
                "UserManagement",
                ["TokenConsumed", "PasswordSet", "AccountActivated", "TokenRejected"]),

            // SES-C1 — one command, three events, two write paths and two
            // actors. The attempt is anonymous and autonomous, because it
            // records a failure and must outlive the transaction that failed;
            // the lock is the system's own act on the command's transaction;
            // the success is the caller's.
            [typeof(SignInCommand)] = new(
                "UserManagement",
                ["SignInSucceeded", "AccountLocked", "SignInFailed"]),

            // PRV-C1 — the tenant's first record, emitted by provisioning
            // rather than by a command. Listed so the start-time check covers
            // it; provisioning validates against the release seed directly,
            // since it runs before any process could have loaded the
            // catalogue it is seeding.
            [typeof(PlatformProvisioning)] = new(
                "UserManagement",
                ["TenantProvisioned"]),

            // PRV-C2 — catalogue synchronisation, emitted by the sync tool on
            // its own connection rather than by a command. ONE code, because
            // the event is about the synchronisation operation: a run emits a
            // single record whether it inserted, changed nothing, or refused.
            // Listed here so the start-time check covers it.
            [typeof(CatalogueSynchronisation)] = new(
                "UserManagement",
                ["PermissionCatalogUpdated"]),
        };

    public static AuditDeclaration? For(Type commandType)
        => ByCommand.GetValueOrDefault(commandType);

    /// <summary>
    /// The start-time check. Throws with every mismatch named, and the host
    /// lets that stop the process (Program.cs).
    /// </summary>
    public static void VerifyAgainst(IAuditEventCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var failures = new List<string>();

        foreach (var (commandType, declaration) in ByCommand)
        {
            foreach (var code in declaration.Codes)
            {
                var definition = catalogue.Find(code, version: 1);

                if (definition is null)
                    failures.Add($"{commandType.Name} declares '{code}' v1, which the catalogue does not contain");
                else if (!definition.IsActive)
                    failures.Add($"{commandType.Name} declares '{code}' v1, which the catalogue has retired");
                else if (definition.OwningContext != declaration.OwningContext)
                    failures.Add($"{commandType.Name} ({declaration.OwningContext}) declares '{code}', owned by {definition.OwningContext}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "The compiled handlers and the deployed audit catalogue disagree, "
                + "so this release cannot run against this database (IMPL-08). "
                + "Either the release's AUD-C3 migration has not been applied, or "
                + "a handler declares an event the release did not seed:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures.Select(x => "  - " + x)));
        }
    }
}

/// <summary>A command's owning context and the catalogue codes it may emit.</summary>
public sealed record AuditDeclaration(string OwningContext, IReadOnlyList<string> Codes);

/// <summary>
/// A marker for an emitter that is not a command: PRV-C1, which writes
/// TenantProvisioned from the provisioning tool on its own transaction.
/// </summary>
public static class PlatformProvisioning
{
}

/// <summary>
/// The second emitter that is not a command: PRV-C2, which writes
/// PermissionCatalogUpdated from the catalogue synchronisation tool on its own
/// connection — independently of the catalogue transaction, so a refused
/// synchronisation keeps the evidence of its refusal.
/// </summary>
public static class CatalogueSynchronisation
{
}
