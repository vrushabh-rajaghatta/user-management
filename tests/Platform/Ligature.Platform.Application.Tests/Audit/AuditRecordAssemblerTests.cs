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
        new("UserManagement", ["UserCreated", "RoleGranted", "SignInFailed", "UsernameChanged", "PolicyChanged", "Thing"]);

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
                Human(), Catalogue(), AuditWritePath.Transactional, Guid.NewGuid(), Now, Now));

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
            [SignInFailed()],
            ActorSnapshot.Anonymous,
            AuditWritePath.Autonomous);

        Assert.Equal("Anonymous", Assert.Single(rows).Actor.OriginKind);
    }

    // ------------------------------------------------------------- ET6

    /// <summary>
    /// The write path is the catalogue's decision, and an emission path may
    /// write only what it can honour. SignInFailed is declared Autonomous
    /// because it records a FAILURE: written on the command's transaction it
    /// would be rolled back with the failure it describes, which is the one
    /// outcome that must never happen to it.
    ///
    /// Until the autonomous writer exists this refusal is the only thing
    /// standing between a catalogue that says Autonomous and a pipeline that
    /// would quietly write it transactionally.
    /// </summary>
    [Fact]
    public void An_autonomous_event_is_refused_on_the_transactional_path()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Assemble([SignInFailed()], ActorSnapshot.Anonymous, AuditWritePath.Transactional));

        Assert.StartsWith("Audit emission defect", failure.Message);

        Assert.Contains(
            "'SignInFailed' is declared Autonomous in the catalogue but this is the Transactional emission path (ET6)",
            failure.Message);

        Assert.Equal(1, CountFailures(failure));
    }

    /// <summary>
    /// And the mirror, so the rule is a comparison rather than a ban on one
    /// value: a transactional event is equally refused on the autonomous path,
    /// which is what E2b will be handed.
    /// </summary>
    [Fact]
    public void A_transactional_event_is_refused_on_the_autonomous_path()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Assemble(
                [new AuditEventDeclaration("UserCreated", 1).Primary("User", Guid.NewGuid()).WithAfter(new { })],
                Human(),
                AuditWritePath.Autonomous));

        Assert.Contains(
            "'UserCreated' is declared Transactional in the catalogue but this is the Autonomous emission path (ET6)",
            failure.Message);

        Assert.Equal(1, CountFailures(failure));
    }

    private static AuditEventDeclaration SignInFailed()
        => new AuditEventDeclaration("SignInFailed", 1)
            .Primary("Identity")
            .WithPayload(new { failureCategory = "CredentialsRejected" });

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

    /// <summary>
    /// IMPL-10. A PII path that describes RefRole:Subject is a promise that
    /// the record carries a Subject ref: it is how the erasure worker later
    /// finds which paths belong to which person. A record making the promise
    /// without the ref would put the path beyond reach of the transformation
    /// the catalogue declares for it.
    ///
    /// The ref is OPTIONAL on this event, so AE3 has nothing to say and the
    /// single failure reported is the one under test. The assertion checks the
    /// count as well as the text, because a test that passes on some other
    /// rule's message proves nothing about this one.
    /// </summary>
    [Fact]
    public void A_PII_path_naming_a_ref_that_was_not_declared_is_a_defect()
    {
        var failure = Defect(
            new AuditEventDeclaration("UsernameChanged", 1)
                .Primary("Identity", Guid.NewGuid())
                .WithBefore(new { Username = "ada" })
                .WithAfter(new { Username = "ada.lovelace" }),
            Human());

        Assert.Contains(
            "'After.Username' describes RefRole:Subject but no ref with role Subject was declared (AE5/IMPL-10)",
            failure.Message);

        Assert.Equal(1, CountFailures(failure));
    }

    [Fact]
    public void The_same_declaration_carrying_the_ref_is_accepted()
    {
        var rows = Assemble(
            [new AuditEventDeclaration("UsernameChanged", 1)
                .Primary("Identity", Guid.NewGuid())
                .Ref("User", Guid.NewGuid(), "Subject")
                .WithBefore(new { Username = "ada" })
                .WithAfter(new { Username = "ada.lovelace" })],
            Human());

        Assert.Equal("UsernameChanged", Assert.Single(rows).EventType);
    }

    /// <summary>
    /// The other half of IMPL-10: a path describing the primary subject on an
    /// event whose primary entity is not a person.
    /// </summary>
    [Fact]
    public void A_PII_path_describing_a_primary_subject_that_is_not_a_person_is_a_defect()
    {
        var failure = Defect(
            new AuditEventDeclaration("PolicyChanged", 1)
                .Primary("SecurityPolicy", Guid.NewGuid())
                .WithAfter(new { Notes = "raised the lockout threshold" }),
            Human());

        Assert.Contains(
            "'After.Notes' describes the primary subject but SecurityPolicy is not a user-subject type (IMPL-10)",
            failure.Message);

        Assert.Equal(1, CountFailures(failure));
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
        ActorSnapshot actor,
        string emissionPath = AuditWritePath.Transactional)
        => AuditRecordAssembler.Assemble(
            declarations, Permitted, actor, Catalogue(), emissionPath, Guid.NewGuid(), Now, Now);

    /// <summary>
    /// Every refusal is an InvalidOperationException whose message opens with
    /// the same phrase. E1 deliberately introduces no AuditEmissionDefect
    /// type: the platform's exception vocabulary is frozen at three, and the
    /// message is what identifies the defect.
    /// </summary>
    private static InvalidOperationException Defect(AuditEventDeclaration declaration, ActorSnapshot actor)
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Assemble([declaration], actor));

        Assert.StartsWith("Audit emission defect", failure.Message);

        return failure;
    }

    /// <summary>
    /// How many rules a defect reports. Each is one bullet in the message.
    /// </summary>
    private static int CountFailures(InvalidOperationException failure)
        => failure.Message
            .Split(Environment.NewLine)
            .Count(x => x.StartsWith("  - ", StringComparison.Ordinal));

    internal static ActorSnapshot Human()
        => new(
            UserId.New(), ActorType.Human, "Ada Lovelace", "ada.lovelace", "ada@example.test",
            "Application", "subject-1",
            null, null, null, null, null, null, Now);

    /// <summary>A small catalogue: four live types and one retired.</summary>
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

            // The ref is optional, so omitting it is an IMPL-10 failure and
            // nothing else.
            new AuditEventTypeDefinition(
                "UsernameChanged", 1, "UserManagement", "IdentityLifecycle", ReasonRequired: false,
                "Transactional", "BeforeAfter", "Identity", PrimaryEntityRequired: true,
                [new("User", "Subject", Required: false)],
                [new("After.Username", "RefRole:Subject")],
                IsActive: true,
                new Dictionary<string, bool> { ["Authenticated"] = true }),

            new AuditEventTypeDefinition(
                "PolicyChanged", 1, "UserManagement", "ConfigurationChange", ReasonRequired: false,
                "Transactional", "BeforeAfter", "SecurityPolicy", PrimaryEntityRequired: true,
                [],
                [new("After.Notes", "PrimarySubject")],
                IsActive: true,
                new Dictionary<string, bool> { ["Authenticated"] = true }),

            new AuditEventTypeDefinition(
                "Thing", 1, "UserManagement", "SecurityEvent", ReasonRequired: false,
                "Transactional", "Payload", "Thing", PrimaryEntityRequired: true,
                [], [],
                IsActive: false,
                new Dictionary<string, bool> { ["Authenticated"] = false }),
        ]);
}
