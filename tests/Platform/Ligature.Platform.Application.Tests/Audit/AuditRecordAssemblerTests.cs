using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Audit;

/// <summary>
/// Behaviours 13 and 14: every rule the pipeline can enforce, refused as a
/// DEFECT with the rule named. A record that fails validation is not
/// evidence, so each test asserts the refusal rather than a degraded record.
/// </summary>
public sealed class AuditRecordAssemblerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly AuditDeclaration Permitted =
        new("UserManagement", ["UserCreated", "RoleGranted", "SignInFailed", "Thing"]);

    // ------------------------------------------------------------- happy path

    [Fact]
    public void A_valid_declaration_becomes_a_row_with_the_captured_catalogue_values()
    {
        var userId = Guid.NewGuid();

        var declaration = new AuditEventDeclaration("UserCreated", 1)
            .Primary("User", userId)
            .WithAfter(new { FirstName = "Ada", LastName = "Lovelace", DisplayName = "Ada", Email = "ada@example.test" });

        var rows = Assemble([declaration], Human());

        var row = Assert.Single(rows);

        Assert.Equal(declaration.AuditId, row.AuditId);
        Assert.Equal("IdentityLifecycle", row.RegulatoryClassification);
        Assert.Equal("Transactional", row.WritePath);
        Assert.False(row.ReasonRequired);
        Assert.Equal("User", row.EntityType);
        Assert.Equal(userId, row.EntityId);
        Assert.Null(row.Before);
        Assert.Equal("""{"DisplayName":"Ada","Email":"ada@example.test","FirstName":"Ada","LastName":"Lovelace"}""", row.After);
        Assert.Null(row.Payload);
    }

    // ------------------------------------------------------- behaviour 13

    [Fact]
    public void An_unknown_code_is_a_defect()
    {
        var failure = Defect(new AuditEventDeclaration("NoSuchEvent", 1).Primary("User", Guid.NewGuid()), Human());

        Assert.Contains("not in the catalogue (AR4)", failure.Message);
    }

    [Fact]
    public void A_retired_version_is_a_defect()
    {
        var failure = Defect(new AuditEventDeclaration("Thing", 1).Primary("Thing", Guid.NewGuid()).WithPayload(new { }), Human());

        Assert.Contains("retired (ET9)", failure.Message);
    }

    [Fact]
    public void A_code_the_command_did_not_register_is_a_defect()
    {
        var declaration = new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid()).WithAfter(new { });

        var failure = Assert.Throws<InvalidOperationException>(() =>
            AuditRecordAssembler.Assemble(
                [declaration], new AuditDeclaration("UserManagement", ["SomethingElse"]),
                Human(), Catalogue(), Guid.NewGuid(), Now, Now));

        Assert.Contains("not among the codes this command is registered to emit (IMPL-08)", failure.Message);
    }

    // ------------------------------------------------------- behaviour 14

    [Fact]
    public void An_anonymous_actor_is_refused_where_the_catalogue_does_not_declare_Anonymous()
    {
        var failure = Defect(new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid()).WithAfter(new { }), ActorSnapshot.Anonymous);

        Assert.Contains("origin Anonymous is not permitted", failure.Message);
    }

    [Fact]
    public void An_anonymous_actor_is_accepted_where_the_catalogue_declares_it()
    {
        var rows = Assemble(
            [new AuditEventDeclaration("SignInFailed", 1).Primary("Identity").WithPayload(new { failureCategory = "CredentialsRejected" })],
            ActorSnapshot.Anonymous);

        Assert.Equal("Anonymous", Assert.Single(rows).Actor.OriginKind);
    }

    [Fact]
    public void A_payload_on_a_BeforeAfter_event_is_a_defect()
    {
        var failure = Defect(new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid()).WithPayload(new { }), Human());

        Assert.Contains("shape is BeforeAfter but a Payload was supplied (AR17)", failure.Message);
    }

    [Fact]
    public void A_missing_primary_id_is_a_defect_where_the_catalogue_requires_one()
    {
        var failure = Defect(new AuditEventDeclaration("UserCreated", 1).Primary("User").WithAfter(new { }), Human());

        Assert.Contains("the catalogue requires a primary entity id and none was declared (AR14)", failure.Message);
    }

    [Fact]
    public void A_primary_type_the_catalogue_does_not_declare_is_a_defect()
    {
        var failure = Defect(new AuditEventDeclaration("UserCreated", 1).Primary("Role", Guid.NewGuid()).WithAfter(new { }), Human());

        Assert.Contains("the catalogue declares User (ET3)", failure.Message);
    }

    [Fact]
    public void An_undeclared_ref_pair_is_a_defect()
    {
        var failure = Defect(
            new AuditEventDeclaration("RoleGranted", 1).Primary("UserRoleAssignment", Guid.NewGuid())
                .Ref("User", Guid.NewGuid(), "Subject").Ref("Role", Guid.NewGuid(), "GrantedRole")
                .Ref("Permission", Guid.NewGuid(), "Granted").WithAfter(new { }).WithReason("because"),
            Human());

        Assert.Contains("ref Permission/Granted is not declared for 'RoleGranted' (AE3)", failure.Message);
    }

    [Fact]
    public void A_missing_required_ref_is_a_defect()
    {
        var failure = Defect(
            new AuditEventDeclaration("RoleGranted", 1).Primary("UserRoleAssignment", Guid.NewGuid())
                .Ref("User", Guid.NewGuid(), "Subject").WithAfter(new { }).WithReason("because"),
            Human());

        Assert.Contains("required ref Role/GrantedRole is missing (AE3)", failure.Message);
    }

    [Fact]
    public void A_ref_that_repeats_the_primary_is_a_defect()
    {
        var id = Guid.NewGuid();

        var failure = Defect(
            new AuditEventDeclaration("RoleGranted", 1).Primary("UserRoleAssignment", id)
                .Ref("User", Guid.NewGuid(), "Subject").Ref("Role", Guid.NewGuid(), "GrantedRole")
                .Ref("UserRoleAssignment", id, "Subject").WithAfter(new { }).WithReason("because"),
            Human());

        Assert.Contains("repeats the primary entity (AE4)", failure.Message);
    }

    [Fact]
    public void A_missing_reason_is_a_defect_where_the_catalogue_requires_one()
    {
        var failure = Defect(
            new AuditEventDeclaration("RoleGranted", 1).Primary("UserRoleAssignment", Guid.NewGuid())
                .Ref("User", Guid.NewGuid(), "Subject").Ref("Role", Guid.NewGuid(), "GrantedRole").WithAfter(new { }),
            Human());

        Assert.Contains("a reason is required and none was supplied (AR9)", failure.Message);
    }

    [Fact]
    public void A_secret_in_the_content_is_a_defect()
    {
        var failure = Defect(
            new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid())
                .WithAfter(new { FirstName = "Ada", password = "hunter2" }),
            Human());

        Assert.Contains("After.password is named like a secret", failure.Message);
    }

    [Fact]
    public void A_partial_actor_snapshot_is_a_defect()
    {
        var partial = Human() with { DisplayName = null };

        var failure = Assert.Throws<InvalidOperationException>(() =>
            Assemble([new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid()).WithAfter(new { })], partial));

        Assert.Contains("all-or-nothing (AR24)", failure.Message);
    }

    // --------------------------------------------------------- ordering

    /// <summary>
    /// AR16 is an immediate foreign key, so an effect must be inserted after
    /// its cause whatever order the handler declared them in.
    /// </summary>
    [Fact]
    public void Causes_are_ordered_before_effects()
    {
        var cause = new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid()).WithAfter(new { });
        var effect = new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid()).WithAfter(new { }).CausedBy(cause);

        var rows = Assemble([effect, cause], Human());

        Assert.Equal([cause.AuditId, effect.AuditId], rows.Select(x => x.AuditId));
    }

    [Fact]
    public void A_causation_cycle_is_a_defect()
    {
        var a = new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid()).WithAfter(new { });
        var b = new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid()).WithAfter(new { }).CausedBy(a);
        a.CausedBy(b);

        var failure = Assert.Throws<InvalidOperationException>(() => Assemble([a, b], Human()));

        Assert.Contains("causation cycle (AR16)", failure.Message);
    }

    // ---------------------------------------------------------- helpers

    private static IReadOnlyList<AuditRecordRow> Assemble(
        IReadOnlyList<AuditEventDeclaration> declarations,
        ActorSnapshot actor)
        => AuditRecordAssembler.Assemble(declarations, Permitted, actor, Catalogue(), Guid.NewGuid(), Now, Now);

    private static InvalidOperationException Defect(AuditEventDeclaration declaration, ActorSnapshot actor)
        => Assert.Throws<InvalidOperationException>(() => Assemble([declaration], actor));

    internal static ActorSnapshot Human()
        => new(
            UserId.New(), ActorType.Human, "Ada Lovelace", "ada.lovelace", "ada@example.test",
            "Application", "subject-1",
            null, null, null, null, null, null, Now);

    /// <summary>A three-type catalogue, plus one retired type.</summary>
    internal static AuditEventCatalogueSnapshot Catalogue()
        => new(
        [
            new AuditEventTypeDefinition(
                "UserCreated", 1, "UserManagement", "IdentityLifecycle", ReasonRequired: false,
                "Transactional", "BeforeAfter", "User", PrimaryEntityRequired: true,
                [],
                [new("After.FirstName", "PrimarySubject"), new("After.Email", "PrimarySubject")],
                IsActive: true,
                new Dictionary<string, bool> { ["Authenticated"] = true, ["System"] = true }),

            new AuditEventTypeDefinition(
                "RoleGranted", 1, "UserManagement", "AuthorisationChange", ReasonRequired: true,
                "Transactional", "BeforeAfter", "UserRoleAssignment", PrimaryEntityRequired: true,
                [new("User", "Subject", Required: true), new("Role", "GrantedRole", Required: true)],
                [],
                IsActive: true,
                new Dictionary<string, bool> { ["Authenticated"] = true, ["System"] = true }),

            new AuditEventTypeDefinition(
                "SignInFailed", 1, "UserManagement", "SecurityEvent", ReasonRequired: false,
                "Autonomous", "Payload", "Identity", PrimaryEntityRequired: false,
                [new("Identity", "Target", Required: false)],
                [new("Payload.IpAddress", "PrimarySubject")],
                IsActive: true,
                new Dictionary<string, bool> { ["Anonymous"] = true }),

            new AuditEventTypeDefinition(
                "Thing", 1, "UserManagement", "SecurityEvent", ReasonRequired: false,
                "Transactional", "Payload", "Thing", PrimaryEntityRequired: true,
                [], [],
                IsActive: false,
                new Dictionary<string, bool> { ["Authenticated"] = false }),
        ]);
}
