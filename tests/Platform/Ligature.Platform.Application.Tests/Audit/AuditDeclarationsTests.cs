using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Commands.AdminResetPassword;
using Ligature.Platform.Application.Users.Commands.ChangePassword;
using Ligature.Platform.Application.Users.Commands.ChangeUserEmail;
using Ligature.Platform.Application.Users.Commands.DeactivateUser;
using Ligature.Platform.Application.Users.Commands.GrantRole;
using Ligature.Platform.Application.Users.Commands.ReactivateUser;
using Ligature.Platform.Application.Users.Commands.RevokeRole;
using Ligature.Platform.Application.Users.Commands.UpdateUserProfile;
using Ligature.Platform.Application.Users.Commands.ReissueActivationLink;
using Ligature.Platform.Application.Users.Commands.RequestPasswordReset;
using Ligature.Platform.Application.Users.Commands.ResetPassword;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Application.Users.Commands.SignOut;
using Ligature.Platform.Application.Users.Commands.RevokeSession;
using Ligature.Platform.Application.Users.Commands.RevokeUserSessions;
using Ligature.Platform.Application.Users.Commands.SignOutEverywhere;
using Ligature.Platform.Application.Users.Commands.UnlockAccount;

namespace Ligature.Platform.Application.Tests.Audit;

/// <summary>
/// IMPL-08's static half: the start-time check that the compiled handlers
/// and the deployed catalogue agree. A mismatch is a refused deployment, not
/// a runtime failure on the first affected command.
/// </summary>
public sealed class AuditDeclarationsTests
{
    [Fact]
    public void USR_C1_declares_its_three_events_under_User_Management()
    {
        var declaration = AuditDeclarations.For(typeof(CreateUserCommand));

        Assert.NotNull(declaration);
        Assert.Equal("UserManagement", declaration!.OwningContext);
        Assert.Equal(["UserCreated", "IdentityCreated", "TokenIssued"], declaration.Codes);
    }

    /// <summary>
    /// PRV-C2 — catalogue synchronisation, the second emitter that is not a
    /// command. One code: the event is about the synchronisation OPERATION, so
    /// a run emits exactly one record whatever it changed.
    /// </summary>
    [Fact]
    public void PRV_C2_declares_PermissionCatalogUpdated()
    {
        var declaration = AuditDeclarations.For(typeof(CatalogueSynchronisation));

        Assert.NotNull(declaration);
        Assert.Equal("UserManagement", declaration!.OwningContext);
        Assert.Equal(["PermissionCatalogUpdated"], declaration.Codes);
    }

    [Fact]
    public void SES_C2_declares_SignedOut()
    {
        var declaration = AuditDeclarations.For(typeof(SignOutCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);
        Assert.Equal(["SignedOut"], declaration.Codes);
    }

    /// <summary>
    /// Three for the bearer who activates, and one for the bearer who does
    /// not: a rejected token is the same command's other outcome.
    /// </summary>
    [Fact]
    public void CRD_C1_declares_both_outcomes()
    {
        var declaration = AuditDeclarations.For(typeof(ActivateAccountCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);

        Assert.Equal(
            ["TokenConsumed", "PasswordSet", "AccountActivated", "TokenRejected"],
            declaration.Codes);
    }

    /// <summary>
    /// SES-C3 and both SES-C4 commands — SessionRevoked, one per session ended.
    /// SignOutEverywhere in particular must never list SignedOut, which belongs
    /// to SES-C2.
    /// </summary>
    [Theory]
    [InlineData(typeof(RevokeSessionCommand))]
    [InlineData(typeof(RevokeUserSessionsCommand))]
    [InlineData(typeof(SignOutEverywhereCommand))]
    public void Session_revocation_commands_declare_only_SessionRevoked(Type command)
    {
        var declaration = AuditDeclarations.For(command);

        Assert.Equal("UserManagement", declaration!.OwningContext);

        Assert.Equal(["SessionRevoked"], declaration.Codes);
    }

    /// <summary>
    /// CRD-C6 — one record per real unlock; refusals declare nothing.
    /// </summary>
    [Fact]
    public void CRD_C6_declares_AccountUnlocked()
    {
        var declaration = AuditDeclarations.For(typeof(UnlockAccountCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);

        Assert.Equal(["AccountUnlocked"], declaration.Codes);
    }

    /// <summary>
    /// CRD-C4 — the change, and one revocation per other session (A5). The
    /// second code is a catalogue amendment recorded in docs/requirements.md.
    /// </summary>
    [Fact]
    public void CRD_C4_declares_the_change_and_the_revocations()
    {
        var declaration = AuditDeclarations.For(typeof(ChangePasswordCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);

        Assert.Equal(["PasswordChanged", "SessionRevoked"], declaration.Codes);
    }

    /// <summary>
    /// CRD-C5 — the administrator's three. No refusal code: an ineligible
    /// target is refused before anything is declared.
    /// </summary>
    [Fact]
    public void CRD_C5_declares_the_issuance_the_token_and_the_supersession()
    {
        var declaration = AuditDeclarations.For(typeof(AdminResetPasswordCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);

        Assert.Equal(
            ["AdminPasswordResetIssued", "TokenIssued", "TokenInvalidated"],
            declaration.Codes);
    }

    /// <summary>
    /// CRD-C7 — existing events only, and no reissue event: the token, and
    /// the supersession of any prior one. The reason travels on TokenIssued.
    /// </summary>
    [Fact]
    public void CRD_C7_declares_the_token_and_the_supersession_and_nothing_else()
    {
        var declaration = AuditDeclarations.For(typeof(ReissueActivationLinkCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);

        Assert.Equal(["TokenIssued", "TokenInvalidated"], declaration.Codes);
    }

    /// <summary>AUT-C1 — one record per grant, carrying the reason.</summary>
    [Fact]
    public void AUT_C1_declares_RoleGranted()
    {
        var declaration = AuditDeclarations.For(typeof(GrantRoleCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);
        Assert.Equal(["RoleGranted"], declaration.Codes);
    }

    /// <summary>AUT-C2 — one record per revocation, carrying the reason.</summary>
    [Fact]
    public void AUT_C2_declares_RoleRevoked()
    {
        var declaration = AuditDeclarations.For(typeof(RevokeRoleCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);
        Assert.Equal(["RoleRevoked"], declaration.Codes);
    }

    /// <summary>USR-C2: one record, and only when something changed.</summary>
    [Fact]
    public void USR_C2_declares_UserProfileChanged()
    {
        var declaration = AuditDeclarations.For(typeof(UpdateUserProfileCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);
        Assert.Equal(["UserProfileChanged"], declaration.Codes);
    }

    /// <summary>
    /// USR-C3: one record, and deliberately NOT TokenInvalidated for the links
    /// it invalidates — none is superseded (D13, as USR-C4).
    /// </summary>
    [Fact]
    public void USR_C3_declares_UserEmailChanged()
    {
        var declaration = AuditDeclarations.For(typeof(ChangeUserEmailCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);
        Assert.Equal(["UserEmailChanged"], declaration.Codes);
    }

    /// <summary>
    /// USR-C4: the cascade's four events, and deliberately NOT TokenInvalidated.
    /// Its frozen definition requires a SupersededBy token and deactivation has
    /// none (D13), so the invalidated tokens are recorded as state only.
    /// </summary>
    [Fact]
    public void USR_C4_declares_the_cascade_and_not_TokenInvalidated()
    {
        var declaration = AuditDeclarations.For(typeof(DeactivateUserCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);
        Assert.Equal(
            ["IdentityDeactivated", "RoleRevoked", "SessionRevoked", "UserDeactivated"],
            declaration.Codes.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("TokenInvalidated", declaration.Codes);
    }

    /// <summary>USR-C5: the user and each identity it returns; nothing is restored.</summary>
    [Fact]
    public void USR_C5_declares_UserReactivated_and_IdentityReactivated()
    {
        var declaration = AuditDeclarations.For(typeof(ReactivateUserCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);
        Assert.Equal(
            ["IdentityReactivated", "UserReactivated"],
            declaration.Codes.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// SES-C1 is the command that needs both write paths and both actors.
    /// </summary>
    [Fact]
    public void SES_C1_declares_the_success_the_lock_and_the_failure()
    {
        var declaration = AuditDeclarations.For(typeof(SignInCommand));

        Assert.Equal("UserManagement", declaration!.OwningContext);

        Assert.Equal(
            ["SignInSucceeded", "AccountLocked", "SignInFailed"],
            declaration.Codes);
    }

    [Fact]
    public void Provisioning_declares_TenantProvisioned()
    {
        var declaration = AuditDeclarations.For(typeof(PlatformProvisioning));

        Assert.Equal(["TenantProvisioned"], declaration!.Codes);
    }

    /// <summary>
    /// The command types are LISTED, not derived — AuditDeclarations exposes
    /// no enumeration, only For(type). The codes are then derived from the
    /// registry, so a command whose codes change cannot leave this asserting
    /// yesterday's set; a command ADDED without being listed here can, and
    /// the only thing that catches that is the host's own start-up
    /// verification against the deployed catalogue.
    ///
    /// (This comment previously claimed the whole thing was derived. It was
    /// not, and adding CRD-C2 is what made the difference visible.)
    /// </summary>
    private static string[] EveryDeclaredCode
        => [.. new[]
            {
                typeof(CreateUserCommand), typeof(SignOutCommand),
                typeof(ActivateAccountCommand), typeof(SignInCommand),
                typeof(RequestPasswordResetCommand), typeof(ResetPasswordCommand),
                typeof(AdminResetPasswordCommand), typeof(ChangePasswordCommand),
                typeof(ReissueActivationLinkCommand),
                typeof(GrantRoleCommand), typeof(RevokeRoleCommand),
                typeof(DeactivateUserCommand), typeof(ReactivateUserCommand),
                typeof(UpdateUserProfileCommand), typeof(ChangeUserEmailCommand),
                typeof(UnlockAccountCommand),
                typeof(RevokeSessionCommand), typeof(RevokeUserSessionsCommand),
                typeof(SignOutEverywhereCommand),
                typeof(PlatformProvisioning), typeof(CatalogueSynchronisation),
            }
            .SelectMany(x => AuditDeclarations.For(x)!.Codes)
            .Distinct()];

    [Fact]
    public void A_catalogue_carrying_every_declared_code_verifies()
    {
        AuditDeclarations.VerifyAgainst(CatalogueWith(EveryDeclaredCode));
    }

    [Fact]
    public void A_catalogue_missing_a_declared_code_refuses_start()
    {
        var incomplete = EveryDeclaredCode.Where(x => x != "TokenIssued").ToArray();

        var failure = Assert.Throws<InvalidOperationException>(() =>
            AuditDeclarations.VerifyAgainst(CatalogueWith(incomplete)));

        Assert.Contains("CreateUserCommand declares 'TokenIssued' v1, which the catalogue does not contain", failure.Message);
        Assert.Contains("IMPL-08", failure.Message);
    }

    [Fact]
    public void A_retired_declared_code_refuses_start()
    {
        var catalogue = CatalogueWith(
            EveryDeclaredCode.Select(x => (x, x != "TokenIssued")).ToArray());

        var failure = Assert.Throws<InvalidOperationException>(() => AuditDeclarations.VerifyAgainst(catalogue));

        Assert.Contains("'TokenIssued' v1, which the catalogue has retired", failure.Message);
    }

    [Fact]
    public void Every_mismatch_is_reported_together()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            AuditDeclarations.VerifyAgainst(CatalogueWith("UserCreated")));

        Assert.Contains("'IdentityCreated'", failure.Message);
        Assert.Contains("'TokenIssued'", failure.Message);
        Assert.Contains("'TenantProvisioned'", failure.Message);
    }

    private static AuditEventCatalogueSnapshot CatalogueWith(params string[] codes)
        => CatalogueWith(codes.Select(x => (x, true)).ToArray());

    private static AuditEventCatalogueSnapshot CatalogueWith(params (string Code, bool Active)[] codes)
        => new(codes.Select(x => new AuditEventTypeDefinition(
            x.Code, 1, "UserManagement", "SecurityEvent", false, "Transactional", "Payload",
            null, false, [], [], x.Active,
            new Dictionary<string, bool> { ["Authenticated"] = true })));
}
