using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Application.Users.Commands.SignOut;

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
    /// Derived from the registry rather than listed, so a command added later
    /// cannot leave this test asserting yesterday's set.
    /// </summary>
    private static string[] EveryDeclaredCode
        => [.. new[]
            {
                typeof(CreateUserCommand), typeof(SignOutCommand),
                typeof(ActivateAccountCommand), typeof(SignInCommand),
                typeof(PlatformProvisioning),
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
