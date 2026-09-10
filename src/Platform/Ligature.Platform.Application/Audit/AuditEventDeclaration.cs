namespace Ligature.Platform.Application.Audit;

/// <summary>
/// One event a handler declares — the handler's half of the emission
/// contract, and nothing of the pipeline's half.
///
/// This is the Design Specification's handler surface (section 8.1), kept
/// literally:
///
///     events.Emit("RoleGranted", version: 1)
///         .Primary("UserRoleAssignment", assignment.Id)
///         .Ref("User", user.Id, role: "Subject")
///         .After(new { scopeType, scopeId, effectiveFrom, effectiveTo })
///         .Reason(command.AssignmentReason);
///
/// A handler names the event, the record whose state it asserts, the other
/// entities involved and the role each played, a state pair or a payload,
/// and a reason. It cannot name the actor, the time, the classification or
/// anything else on the envelope: those the pipeline supplies, and the
/// envelope is closed (emission contract, "Anything else — not permitted").
///
/// The AuditId is assigned here, at declaration, rather than at insert
/// (IMPL-01): a later declaration in the same command can then name an
/// earlier one as its cause, and the writer orders the batch so the cause is
/// inserted first — AR16 is an immediate foreign key.
/// </summary>
public sealed class AuditEventDeclaration
{
    private readonly List<AuditEntityReference> _references = [];

    public AuditEventDeclaration(string code, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        Code = code;
        Version = version;
        AuditId = Guid.CreateVersion7();
    }

    public Guid AuditId { get; }

    public string Code { get; }

    public int Version { get; }

    public string? PrimaryEntityType { get; private set; }

    public Guid? PrimaryEntityId { get; private set; }

    public IReadOnlyList<AuditEntityReference> References => _references;

    public object? Before { get; private set; }

    public object? After { get; private set; }

    public object? Payload { get; private set; }

    public string? Reason { get; private set; }

    public Guid? CausationId { get; private set; }

    /// <summary>
    /// Whether this event is attributed to the System actor rather than to
    /// the command's caller. See <see cref="AsSystem"/>.
    /// </summary>
    public bool AttributedToSystem { get; private set; }

    /// <summary>
    /// Attribute this ONE event to the System actor.
    ///
    /// An attribution override, not an impersonation mechanism. Frozen
    /// (E2b): the only actor a declaration may name is System, and there is
    /// deliberately no As(userId) beside it — a handler that could name any
    /// actor could write a record blaming somebody.
    ///
    /// It exists because a single command can produce events with different
    /// actors. A failing sign-in that crosses the lockout threshold produces
    /// two: the attempt, which nobody authenticated and which is therefore
    /// anonymous, and the lock, which the system imposed by policy and which
    /// no caller asked for. Attributing the lock to the person who mistyped
    /// their password would say they locked their own account; attributing it
    /// to nobody would lose who did.
    ///
    /// The catalogue is the second line of defence: the origin rule refuses
    /// this wherever System is not a permitted origin for the event type.
    /// </summary>
    public AuditEventDeclaration AsSystem()
    {
        AttributedToSystem = true;

        return this;
    }

    /// <summary>
    /// The record whose state this event asserts (AUD-D27). The id may be
    /// omitted only for event types the catalogue marks as not requiring one
    /// — tenant- and catalogue-level events; behaviour 14 checks.
    /// </summary>
    public AuditEventDeclaration Primary(string entityType, Guid? entityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);

        PrimaryEntityType = entityType;
        PrimaryEntityId = entityId;

        return this;
    }

    /// <summary>
    /// Another entity the event involved, with the role it played. The
    /// primary is not repeated here (AE4); the pair must be one the catalogue
    /// declares for this event type (AE3).
    /// </summary>
    public AuditEventDeclaration Ref(string entityType, Guid entityId, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        _references.Add(new AuditEntityReference(entityType, entityId, role));

        return this;
    }

    /// <summary>State before the change. Null for creations.</summary>
    public AuditEventDeclaration WithBefore(object before)
    {
        Before = before ?? throw new ArgumentNullException(nameof(before));

        return this;
    }

    /// <summary>State after the change.</summary>
    public AuditEventDeclaration WithAfter(object after)
    {
        After = after ?? throw new ArgumentNullException(nameof(after));

        return this;
    }

    /// <summary>
    /// Event-specific information that is not a state diff. Mutually
    /// exclusive with a Before/After pair (AR17); the catalogue says which
    /// shape an event type carries.
    /// </summary>
    public AuditEventDeclaration WithPayload(object payload)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));

        return this;
    }

    /// <summary>A human explanation from the command input — never a code (AUD-7).</summary>
    public AuditEventDeclaration WithReason(string? reason)
    {
        Reason = reason;

        return this;
    }

    /// <summary>
    /// The record this event is a reaction to (behaviour 18): a cascade step
    /// names the event that started the cascade, a lockout names the failure
    /// that tripped it. Usually another declaration in the same command.
    /// </summary>
    public AuditEventDeclaration CausedBy(AuditEventDeclaration cause)
    {
        ArgumentNullException.ThrowIfNull(cause);

        CausationId = cause.AuditId;

        return this;
    }

    public AuditEventDeclaration CausedBy(Guid auditId)
    {
        CausationId = auditId;

        return this;
    }
}

/// <summary>An audit_entity_ref row as the handler declares it (AE1-AE4).</summary>
public sealed record AuditEntityReference(string EntityType, Guid EntityId, string Role);
