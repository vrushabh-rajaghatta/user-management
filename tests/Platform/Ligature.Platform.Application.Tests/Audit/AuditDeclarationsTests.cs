using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Users.Commands.CreateUser;

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
    public void Provisioning_declares_TenantProvisioned()
    {
        var declaration = AuditDeclarations.For(typeof(PlatformProvisioning));

        Assert.Equal(["TenantProvisioned"], declaration!.Codes);
    }

    [Fact]
    public void A_catalogue_carrying_every_declared_code_verifies()
    {
        AuditDeclarations.VerifyAgainst(CatalogueWith(
            "UserCreated", "IdentityCreated", "TokenIssued", "TenantProvisioned"));
    }

    [Fact]
    public void A_catalogue_missing_a_declared_code_refuses_start()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            AuditDeclarations.VerifyAgainst(CatalogueWith("UserCreated", "IdentityCreated", "TenantProvisioned")));

        Assert.Contains("CreateUserCommand declares 'TokenIssued' v1, which the catalogue does not contain", failure.Message);
        Assert.Contains("IMPL-08", failure.Message);
    }

    [Fact]
    public void A_retired_declared_code_refuses_start()
    {
        var catalogue = CatalogueWith(
            ("UserCreated", true), ("IdentityCreated", true), ("TokenIssued", false), ("TenantProvisioned", true));

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
