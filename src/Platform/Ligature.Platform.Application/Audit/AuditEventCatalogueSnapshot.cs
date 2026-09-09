namespace Ligature.Platform.Application.Audit;

/// <summary>
/// The event catalogue as the pipeline reads it: an immutable, in-memory map
/// of audit_event_type and audit_event_origin keyed by (Code, Version).
///
/// Loaded once per process (IMPL-08). The catalogue is release-controlled
/// data that changes only when AUD-C3 runs during a deployment, before the
/// application resumes, so a start-time load is sufficient and there is no
/// in-process invalidation to get wrong. Reading it once, centrally, is what
/// makes AUD-8 hold: every record captures the classification, reason rule
/// and write path in force when it was written, from one source.
/// </summary>
public interface IAuditEventCatalogue
{
    AuditEventTypeDefinition? Find(string code, int version);

    IReadOnlyCollection<AuditEventTypeDefinition> All { get; }
}

/// <summary>One row of audit_event_type with its audit_event_origin rows.</summary>
public sealed record AuditEventTypeDefinition(
    string Code,
    int Version,
    string OwningContext,
    string DefaultClassification,
    bool ReasonRequired,
    string WritePath,
    string Shape,
    string? PrimaryEntityType,
    bool PrimaryEntityRequired,
    IReadOnlyList<AuditEntityRefRole> EntityRefRoles,
    IReadOnlyList<AuditPiiPath> PiiPaths,
    bool IsActive,
    IReadOnlyDictionary<string, bool> Origins)
{
    /// <summary>Whether this type may be written with the given origin kind, and that origin is still active (AR5, EO7).</summary>
    public bool PermitsOrigin(string originKind)
        => Origins.TryGetValue(originKind, out var active) && active;
}

/// <summary>ET4 — a (EntityType, RefRole) pair a record may carry, and whether it must.</summary>
public sealed record AuditEntityRefRole(string EntityType, string RefRole, bool Required);

/// <summary>ET5 — a transformable path and whom it describes: Actor, PrimarySubject or RefRole:&lt;role&gt;.</summary>
public sealed record AuditPiiPath(string Path, string Describes);

/// <summary>
/// An immutable catalogue built from definitions, whichever source supplied
/// them — the database at process start, or the release seed during
/// provisioning, when the rows have been written but not yet committed.
/// </summary>
public sealed class AuditEventCatalogueSnapshot : IAuditEventCatalogue
{
    private readonly Dictionary<(string Code, int Version), AuditEventTypeDefinition> _byKey;

    public AuditEventCatalogueSnapshot(IEnumerable<AuditEventTypeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _byKey = definitions.ToDictionary(x => (x.Code, x.Version));
    }

    public AuditEventTypeDefinition? Find(string code, int version)
        => _byKey.GetValueOrDefault((code, version));

    public IReadOnlyCollection<AuditEventTypeDefinition> All => _byKey.Values;
}
