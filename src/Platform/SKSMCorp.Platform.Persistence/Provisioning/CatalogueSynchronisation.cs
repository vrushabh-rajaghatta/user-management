namespace SKSMCorp.Platform.Persistence.Provisioning;

/// <summary>
/// What a synchronisation run did (PRV-C2). Three outcomes, and the third is
/// not a failure: a database with no System actor has no catalogue to
/// reconcile, which is the clean-bootstrap path rather than drift.
/// </summary>
public enum CatalogueSyncOutcome
{
    /// <summary>Applied, or found nothing to apply. Both are success.</summary>
    Succeeded,

    /// <summary>Drift the release is not entitled to resolve. Nothing was applied.</summary>
    Refused,

    /// <summary>No System actor: nothing to reconcile, and nothing recorded.</summary>
    NotProvisioned,
}

/// <summary>
/// The closed set of refusal conditions (docs/requirements.md, PRV-C2).
///
/// Closed on purpose: the audit event carries these codes with a count each, so
/// the payload stays bounded however large the drift is. A seventh condition
/// means the contract changed, not that a code was missing.
/// </summary>
public enum CatalogueRefusalReason
{
    /// <summary>A permission in the database, absent from the catalogue (F2).</summary>
    PermissionMissingFromSeed,

    /// <summary>A role in the database, absent from the catalogue (F2).</summary>
    RoleMissingFromSeed,

    /// <summary>An active grant in the database, absent from the catalogue (F5).</summary>
    GrantMissingFromSeed,

    /// <summary>A permission or role inactive in the database and listed in the catalogue (F4).</summary>
    InactiveCatalogueEntry,

    /// <summary>A grant revoked in the database and listed in the catalogue (F6).</summary>
    RevokedGrantInSeed,

    /// <summary>Code, Resource, Action, RequiresHumanActor or IsSystemRole differs (F7).</summary>
    SecuritySemanticDrift,
}

/// <summary>
/// One finding. <paramref name="Subject"/> is the code it concerns — a
/// permission code, a role code, or "roleCode/permissionCode" for a grant.
/// </summary>
public sealed record CatalogueRefusal(CatalogueRefusalReason Reason, string Subject);

/// <summary>
/// The four permitted mutations, counted (M1-M4).
///
/// There is no role-metadata count, and its absence is the contract rather than
/// an omission: M5 was withdrawn because every seeded role is a system role and
/// the domain refuses to modify one. A count that could only ever be zero would
/// tell a reader something false about what this does.
/// </summary>
public sealed record CatalogueSyncCounts(
    int PermissionsInserted,
    int PermissionMetadataReconciled,
    int RolesInserted,
    int GrantsInserted)
{
    public static CatalogueSyncCounts None { get; } = new(0, 0, 0, 0);

    public int Total
        => PermissionsInserted + PermissionMetadataReconciled + RolesInserted + GrantsInserted;
}

/// <summary>
/// The outcome of one run. Counts are what was COMMITTED, so a refusal carries
/// none: refusals are detected before any mutation is applied.
/// </summary>
public sealed record CatalogueSyncResult(
    CatalogueSyncOutcome Outcome,
    CatalogueSyncCounts Counts,
    IReadOnlyList<CatalogueRefusal> Refusals)
{
    /// <summary>
    /// The distinct reasons found, with how many findings each covers.
    ///
    /// This is ALL the audit payload carries about a refusal. A digest of the
    /// findings was specified and removed: behaviour 14's secret scan rejects a
    /// hex digest of 40 characters or more, so a record carrying one is never
    /// written, and encoding it to slip past the scan would be designing around
    /// a frozen security control. The subjects stay with the operator.
    /// </summary>
    public IReadOnlyList<(CatalogueRefusalReason Reason, int Count)> RefusalsByReason
        => [.. Refusals
            .GroupBy(x => x.Reason)
            .OrderBy(x => x.Key)
            .Select(x => (x.Key, x.Count()))];
}

/// <summary>
/// The catalogue was synchronised and committed, and its audit record could
/// not be written.
///
/// Deliberately its own type, because the two are materially different: a
/// refusal changed nothing, and this changed authorization and failed to
/// record that it had. Nothing is compensated — the transaction is already
/// committed, and inventing reversal SQL would turn a reporting failure into
/// an authorization change nobody asked for.
/// </summary>
public sealed class CatalogueAuditNotRecordedException : Exception
{
    public CatalogueAuditNotRecordedException(CatalogueSyncResult result, Exception inner)
        : base(MessageFor(result), inner)
    {
        Result = result;
    }

    /// <summary>The outcome that failed to be recorded, so the caller can report it.</summary>
    public CatalogueSyncResult Result { get; }

    /// <summary>
    /// The two cases are materially different and the message says which.
    /// A refused run committed NOTHING, so telling an operator that catalogue
    /// changes are applied and unreversed would send them looking for damage
    /// that does not exist. An unconditional "committed" was the first draft of
    /// this message, and it was wrong in exactly the case that matters most.
    /// </summary>
    private static string MessageFor(CatalogueSyncResult result)
        => result.Outcome switch
        {
            CatalogueSyncOutcome.Succeeded =>
                "Catalogue synchronisation SUCCEEDED, and its audit record could not be written. "
                + "The catalogue changes are committed and are NOT reversed; the trail does not "
                + "record them. Investigate before the next deployment.",

            _ =>
                "Catalogue synchronisation was REFUSED, and its audit record could not be written. "
                + "No catalogue mutation was committed — there is nothing to undo — but the refusal "
                + "is not in the trail. The refusal itself is reported above.",
        };
}

