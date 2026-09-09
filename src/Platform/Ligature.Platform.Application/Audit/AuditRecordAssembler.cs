using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Audit;

/// <summary>
/// Behaviours 13, 14 and 16 in one place: takes what the handler declared
/// and what the pipeline knows, resolves each declaration against the
/// catalogue, validates it, and produces the rows the writer inserts —
/// content already canonical, causes before effects.
///
/// Used by behaviour 7 for every command and by provisioning for the one
/// emission that is not a command, so that both are held to the same rules.
/// A record that fails validation is not evidence; refusing to write it is
/// safer than writing it wrong.
///
/// Every refusal is a DEFECT — an <see cref="InvalidOperationException"/>
/// naming the rule — never a domain error (section 15.1). The exception
/// vocabulary is frozen at three domain types (docs/architecture.md section
/// 11), and a defect is not a rule a user broke: it is a handler, a
/// registration or a catalogue that disagree, which a retry cannot fix.
/// </summary>
public static class AuditRecordAssembler
{
    /// <summary>Types whose subject is a user, for IMPL-10's PrimarySubject rule.</summary>
    private static readonly HashSet<string> UserSubjectTypes = new(StringComparer.Ordinal)
    {
        "User", "Identity", "Credential", "Token", "Session", "UserRoleAssignment",
    };

    public static IReadOnlyList<AuditRecordRow> Assemble(
        IReadOnlyList<AuditEventDeclaration> declarations,
        AuditDeclaration permitted,
        ActorSnapshot actor,
        IAuditEventCatalogue catalogue,
        Guid operationId,
        DateTimeOffset occurredAt,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(permitted);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(catalogue);

        ValidateSnapshot(actor);

        var rows = declarations
            .Select(d => Resolve(d, permitted, actor, catalogue, operationId, occurredAt, capturedAt))
            .ToList();

        return OrderCausally(rows);
    }

    private static AuditRecordRow Resolve(
        AuditEventDeclaration declaration,
        AuditDeclaration permitted,
        ActorSnapshot actor,
        IAuditEventCatalogue catalogue,
        Guid operationId,
        DateTimeOffset occurredAt,
        DateTimeOffset capturedAt)
    {
        var failures = new List<string>();

        // Behaviour 13 — resolve, and refuse what cannot be resolved.
        var type = catalogue.Find(declaration.Code, declaration.Version);

        if (type is null)
            throw Defect(declaration, [$"'{declaration.Code}' v{declaration.Version} is not in the catalogue (AR4)"]);

        if (!type.IsActive)
            failures.Add($"'{declaration.Code}' v{declaration.Version} is retired (ET9)");

        if (!permitted.Codes.Contains(declaration.Code, StringComparer.Ordinal))
            failures.Add($"'{declaration.Code}' is not among the codes this command is registered to emit (IMPL-08)");

        if (type.OwningContext != permitted.OwningContext)
            failures.Add($"'{declaration.Code}' is owned by {type.OwningContext}; this emitter is {permitted.OwningContext}");

        // Behaviour 14 — origin (AR5, AR6, EO7)
        var origin = actor.OriginKind;

        if (!type.PermitsOrigin(origin))
            failures.Add($"origin {origin} is not permitted for '{declaration.Code}' (AR5/EO5)");

        // Shape (AR17, ET3)
        var hasPair = declaration.Before is not null || declaration.After is not null;
        var hasPayload = declaration.Payload is not null;

        switch (type.Shape)
        {
            case "BeforeAfter" when hasPayload:
                failures.Add("shape is BeforeAfter but a Payload was supplied (AR17)");
                break;
            case "BeforeAfter" when !hasPair:
                failures.Add("shape is BeforeAfter but neither Before nor After was supplied (AR17)");
                break;
            case "Payload" when hasPair:
                failures.Add("shape is Payload but Before/After was supplied (AR17)");
                break;
            case "Payload" when !hasPayload:
                failures.Add("shape is Payload but no Payload was supplied (AR17)");
                break;
            case "None" when hasPair || hasPayload:
                failures.Add("shape is None but content was supplied (AR17)");
                break;
        }

        // Primary entity (AR14, ET3)
        if (declaration.PrimaryEntityType is null)
            failures.Add("no primary entity type was declared (AR14)");
        else if (type.PrimaryEntityType is not null && declaration.PrimaryEntityType != type.PrimaryEntityType)
            failures.Add($"primary entity is {declaration.PrimaryEntityType}; the catalogue declares {type.PrimaryEntityType} (ET3)");

        if (type.PrimaryEntityRequired && declaration.PrimaryEntityId is null)
            failures.Add("the catalogue requires a primary entity id and none was declared (AR14)");

        // References (AE3, AE4)
        foreach (var reference in declaration.References)
        {
            if (!type.EntityRefRoles.Any(x => x.EntityType == reference.EntityType && x.RefRole == reference.Role))
                failures.Add($"ref {reference.EntityType}/{reference.Role} is not declared for '{declaration.Code}' (AE3)");

            if (reference.EntityType == declaration.PrimaryEntityType && reference.EntityId == declaration.PrimaryEntityId)
                failures.Add($"ref {reference.EntityType}/{reference.Role} repeats the primary entity (AE4)");
        }

        foreach (var required in type.EntityRefRoles.Where(x => x.Required))
        {
            if (!declaration.References.Any(x => x.EntityType == required.EntityType && x.Role == required.RefRole))
                failures.Add($"required ref {required.EntityType}/{required.RefRole} is missing (AE3)");
        }

        // IMPL-10 — PII paths imply refs
        foreach (var pii in type.PiiPaths)
        {
            if (pii.Describes.StartsWith("RefRole:", StringComparison.Ordinal))
            {
                var role = pii.Describes["RefRole:".Length..];

                if (!declaration.References.Any(x => x.Role == role))
                    failures.Add($"'{pii.Path}' describes {pii.Describes} but no ref with role {role} was declared (AE5/IMPL-10)");
            }
            else if (pii.Describes == "PrimarySubject"
                     && (declaration.PrimaryEntityType is null || !UserSubjectTypes.Contains(declaration.PrimaryEntityType)))
            {
                failures.Add($"'{pii.Path}' describes the primary subject but {declaration.PrimaryEntityType ?? "<none>"} is not a user-subject type (IMPL-10)");
            }
        }

        // Reason (AR9)
        if (type.ReasonRequired && string.IsNullOrWhiteSpace(declaration.Reason))
            failures.Add("a reason is required and none was supplied (AR9)");

        // Content: canonical form (IMPL-03) and the secret scan
        var before = Canonical(declaration.Before, "Before", failures);
        var after = Canonical(declaration.After, "After", failures);
        var payload = Canonical(declaration.Payload, "Payload", failures);

        if (failures.Count > 0)
            throw Defect(declaration, failures);

        return new AuditRecordRow(
            declaration.AuditId,
            occurredAt,
            capturedAt,
            type.Code,
            type.Version,
            type.WritePath,
            type.DefaultClassification,
            type.ReasonRequired,
            declaration.Reason,
            actor,
            declaration.PrimaryEntityType!,
            declaration.PrimaryEntityId,
            operationId,
            declaration.CausationId,
            before,
            after,
            payload,
            declaration.References);
    }

    private static string? Canonical(object? content, string name, List<string> failures)
    {
        if (content is null)
            return null;

        try
        {
            var element = CanonicalJson.ToElement(content);

            foreach (var finding in SecretScan.Scan(element, name))
                failures.Add($"{finding} (behaviour 14 secret scan)");

            return CanonicalJson.Canonicalize(element);
        }
        catch (InvalidOperationException failure)
        {
            failures.Add($"{name} cannot be canonicalised: {failure.Message}");

            return null;
        }
    }

    /// <summary>
    /// AR11 and AR24, checked before the database does so the defect names
    /// the rule. The snapshot group is all-present or all-absent; email is
    /// Human-only; a non-System actor was vouched for by a provider.
    /// </summary>
    private static void ValidateSnapshot(ActorSnapshot actor)
    {
        var failures = new List<string>();

        var present = new[] { actor.UserId is not null, actor.ActorType is not null, actor.DisplayName is not null, actor.CapturedAt is not null };

        if (present.Distinct().Count() > 1)
            failures.Add("the actor snapshot is partial: UserId, ActorType, DisplayName and CapturedAt are all-or-nothing (AR24)");

        if (actor.Email is not null && actor.ActorType != ActorType.Human)
            failures.Add("only a Human actor carries an email (AR11)");

        if (actor.UserId is not null && actor.ActorType != ActorType.System
            && (actor.IdentityProvider is null || actor.SubjectId is null))
            failures.Add("a non-System actor must carry its identity provider and subject id (AR11)");

        // AR13 — PlatformAccessRef is present exactly for a PlatformOperator.
        // Not checked here: the PlatformOperator actor type does not exist in
        // the domain until User Management widens app_user under AM-01, so
        // there is no value to compare against. The database CHECK enforces
        // it regardless; this method gains the clause when the type does.
        if (actor.PlatformAccessRef is not null)
            failures.Add("PlatformAccessRef was supplied but no actor type may carry one yet (AR13; PlatformOperator awaits AM-01)");

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Audit emission defect — the actor snapshot is malformed:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures.Select(x => "  - " + x)));
        }
    }

    /// <summary>
    /// Causes before effects, so the immediate AR16 foreign key resolves on
    /// insert. A cause outside the batch is assumed already committed; the
    /// key will say otherwise if not.
    /// </summary>
    private static IReadOnlyList<AuditRecordRow> OrderCausally(List<AuditRecordRow> rows)
    {
        var ids = rows.Select(x => x.AuditId).ToHashSet();
        var placed = new HashSet<Guid>();
        var ordered = new List<AuditRecordRow>(rows.Count);

        while (ordered.Count < rows.Count)
        {
            var progressed = false;

            foreach (var row in rows)
            {
                if (placed.Contains(row.AuditId))
                    continue;

                var causeInBatch = row.CausationId is { } cause && ids.Contains(cause);

                if (!causeInBatch || placed.Contains(row.CausationId!.Value))
                {
                    ordered.Add(row);
                    placed.Add(row.AuditId);
                    progressed = true;
                }
            }

            if (!progressed)
            {
                throw new InvalidOperationException(
                    "Audit emission defect — the declared events form a causation cycle (AR16).");
            }
        }

        return ordered;
    }

    private static InvalidOperationException Defect(
        AuditEventDeclaration declaration,
        IReadOnlyList<string> failures)
        => new(
            $"Audit emission defect — '{declaration.Code}' v{declaration.Version} "
            + "cannot be written, and the command has been rolled back. A record "
            + "that fails validation is not evidence:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures.Select(x => "  - " + x)));
}

/// <summary>
/// One audit_record row and its audit_entity_ref rows, fully resolved and
/// ready to insert. Content columns are already canonical text.
/// </summary>
public sealed record AuditRecordRow(
    Guid AuditId,
    DateTimeOffset OccurredAt,
    DateTimeOffset CapturedAt,
    string EventType,
    int EventVersion,
    string WritePath,
    string RegulatoryClassification,
    bool ReasonRequired,
    string? Reason,
    ActorSnapshot Actor,
    string EntityType,
    Guid? EntityId,
    Guid OperationId,
    Guid? CausationId,
    string? Before,
    string? After,
    string? Payload,
    IReadOnlyList<AuditEntityReference> References);
