using System.Text.Json;
using Ligature.Platform.Domain.Audit;
using Ligature.Platform.Domain.Users;
using Npgsql;
using NpgsqlTypes;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// Writes the release's audit catalogue and the tenant's retention policy v1,
/// which are now applied in DIFFERENT phases from the SAME source.
///
/// <code>
/// AuditEventCatalogue.GetEventTypeSeeds()
///   ├── deployment (Ligature.AuditSchema, as audit_owner, before the host)
///   │     └── ReconcileCatalogueAsync — event types and origins
///   └── provisioning (AUD-C4, as provisioning_role, inside PRV-C1)
///         └── SeedRetentionVersionOneAsync — retention v1
/// </code>
///
/// The catalogue moved into the deployment phase because the compiled
/// handlers depend on it: IMPL-08 verifies their declarations at host
/// startup, so a host on a fresh database could never start while the
/// catalogue was written by a later bootstrap step. It is release-controlled
/// infrastructure, not tenant data. Retention v1 stayed, because RT5 ties it
/// to an actor that provisioning creates.
///
/// There is ONE definition. This class applies it in two places; it does not
/// hold two copies of it.
///
/// Raw SQL on a connection and transaction the caller already holds — not an
/// EF entity, not a DbSet, not a repository. The audit tables belong to
/// audit_owner, and the application writes into them without modelling them
/// as its own state (docs/architecture.md section 19). This is deliberately
/// the same mechanism the emission writer will use: rows go into a schema
/// the writer does not own, on the transaction it is already inside, so a
/// rollback of the business work takes the audit work with it.
///
/// Emits nothing. At tenant creation the catalogue is being ESTABLISHED, not
/// changed; TenantProvisioned records the catalogue and retention versions
/// in its payload, and a later release that changes the catalogue runs
/// AUD-C3, which does emit. Seeding here and emitting nothing is what keeps
/// those two operations distinct.
/// </summary>
internal sealed class AuditCatalogueSeeder
{
    // No naming policy: each seed record names its own keys, in the casing the
    // workbook uses for that structure (ET4 PascalCase, ET5 lower case).
    private static readonly JsonSerializerOptions Json = new();

    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    public AuditCatalogueSeeder(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        _connection = connection;
        _transaction = transaction;
    }

    /// <summary>
    /// RECONCILES the catalogue against the release seed, rather than
    /// inserting into an empty one.
    ///
    /// This used to be plain INSERTs that failed loudly on a duplicate, which
    /// was right while it ran once per tenant inside PRV-C1's System-actor
    /// sentinel. It now runs in the deployment phase, before the host starts,
    /// on EVERY deployment — so a catalogue that already holds the previous
    /// release's rows is the normal case, not a broken assumption.
    ///
    /// Reconciling means: entries new in this release are inserted, entries
    /// whose definition changed are updated, and entries the release no longer
    /// declares are marked INACTIVE. Nothing is ever deleted. A committed
    /// audit_record references its event type, and a catalogue that could drop
    /// rows would be a catalogue that could orphan the trail.
    /// </summary>
    public async Task ReconcileCatalogueAsync(CancellationToken cancellationToken)
    {
        var seeds = AuditEventCatalogue.GetEventTypeSeeds();

        foreach (var seed in seeds)
        {
            await UpsertEventTypeAsync(seed, cancellationToken);

            // Origins after their type: EO1 is a foreign key.
            foreach (var origin in seed.Origins)
            {
                await UpsertOriginAsync(seed, origin, cancellationToken);
            }
        }

        await RetireAbsentEventTypesAsync(seeds, cancellationToken);
        await RetireAbsentOriginsAsync(seeds, cancellationToken);
    }

    /// <summary>
    /// Retention v1 stays with provisioning and does NOT move into the
    /// deployment phase, because RT5 (script 003) makes created_by a foreign
    /// key to public.app_user and the System actor it names is created by
    /// PRV-C1. Seeding it before the host would fail that key on a fresh
    /// database. The split is therefore a constraint, not a preference.
    /// </summary>
    public async Task SeedRetentionVersionOneAsync(
        DateTimeOffset provisionedAt,
        UserId createdBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createdBy);

        await InsertRetentionVersionOneAsync(
            provisionedAt, createdBy, cancellationToken);
    }

    /// <summary>
    /// Marks inactive every event type the release no longer declares. Set
    /// membership is passed as two parallel arrays rather than built into the
    /// SQL text, so a catalogue entry can never be a parameter injection site.
    /// </summary>
    private async Task RetireAbsentEventTypesAsync(
        IReadOnlyList<EventTypeSeed> seeds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE audit.audit_event_type AS t
               SET is_active = false
             WHERE t.is_active
               AND NOT EXISTS (
                   SELECT 1
                     FROM unnest(@codes, @versions) AS s(code, version)
                    WHERE s.code = t.code AND s.version = t.version)
            """,
            _connection,
            _transaction);

        command.Parameters.Add("codes", NpgsqlDbType.Array | NpgsqlDbType.Text).Value =
            seeds.Select(x => x.Code).ToArray();
        command.Parameters.Add("versions", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value =
            seeds.Select(x => x.Version).ToArray();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The same for origins, keyed on the triple that identifies one (EO1).
    /// </summary>
    private async Task RetireAbsentOriginsAsync(
        IReadOnlyList<EventTypeSeed> seeds,
        CancellationToken cancellationToken)
    {
        var codes = new List<string>();
        var versions = new List<int>();
        var kinds = new List<string>();

        foreach (var seed in seeds)
        {
            foreach (var origin in seed.Origins)
            {
                codes.Add(seed.Code);
                versions.Add(seed.Version);
                kinds.Add(origin);
            }
        }

        await using var command = new NpgsqlCommand(
            """
            UPDATE audit.audit_event_origin AS o
               SET is_active = false
             WHERE o.is_active
               AND NOT EXISTS (
                   SELECT 1
                     FROM unnest(@codes, @versions, @kinds) AS s(code, version, origin_kind)
                    WHERE s.code = o.code
                      AND s.version = o.version
                      AND s.origin_kind = o.origin_kind)
            """,
            _connection,
            _transaction);

        command.Parameters.Add("codes", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = codes.ToArray();
        command.Parameters.Add("versions", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = versions.ToArray();
        command.Parameters.Add("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = kinds.ToArray();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertEventTypeAsync(
        EventTypeSeed seed,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_event_type
                (code, version, owning_context, name, description,
                 default_classification, reason_required, write_path, shape,
                 primary_entity_type, primary_entity_required,
                 entity_ref_roles, pii_paths, payload_schema_ref, is_active)
            VALUES
                (@code, @version, @context, @name, NULL,
                 @classification, @reason_required, @write_path, @shape,
                 @primary_type, @primary_required,
                 @entity_ref_roles, @pii_paths, NULL, @is_active)
            ON CONFLICT (code, version) DO UPDATE SET
                owning_context         = EXCLUDED.owning_context,
                name                   = EXCLUDED.name,
                description            = EXCLUDED.description,
                default_classification = EXCLUDED.default_classification,
                reason_required        = EXCLUDED.reason_required,
                write_path             = EXCLUDED.write_path,
                shape                  = EXCLUDED.shape,
                primary_entity_type    = EXCLUDED.primary_entity_type,
                primary_entity_required= EXCLUDED.primary_entity_required,
                entity_ref_roles       = EXCLUDED.entity_ref_roles,
                pii_paths              = EXCLUDED.pii_paths,
                payload_schema_ref     = EXCLUDED.payload_schema_ref,
                is_active              = EXCLUDED.is_active
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("code", seed.Code);
        command.Parameters.AddWithValue("version", seed.Version);
        command.Parameters.AddWithValue("context", seed.OwningContext);
        command.Parameters.AddWithValue("name", seed.Name);
        command.Parameters.AddWithValue("classification", seed.DefaultClassification);
        command.Parameters.AddWithValue("reason_required", seed.ReasonRequired);
        command.Parameters.AddWithValue("write_path", seed.WritePath);
        command.Parameters.AddWithValue("shape", seed.Shape);
        command.Parameters.AddWithValue(
            "primary_type", (object?)seed.PrimaryEntityType ?? DBNull.Value);
        command.Parameters.AddWithValue("primary_required", seed.PrimaryEntityRequired);
        command.Parameters.Add("entity_ref_roles", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(seed.EntityRefRoles, Json);
        command.Parameters.Add("pii_paths", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(seed.PiiPaths, Json);
        command.Parameters.AddWithValue("is_active", seed.IsActive);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// An origin row's activity follows its type's. A deferred type's origins
    /// are seeded inactive with it, so that when AU11 lifts the deferral both
    /// are switched on by the same AUD-C3 run.
    /// </summary>
    private async Task UpsertOriginAsync(
        EventTypeSeed seed,
        string originKind,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_event_origin (code, version, origin_kind, is_active)
            VALUES (@code, @version, @origin_kind, @is_active)
            ON CONFLICT (code, version, origin_kind) DO UPDATE SET
                is_active = EXCLUDED.is_active
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("code", seed.Code);
        command.Parameters.AddWithValue("version", seed.Version);
        command.Parameters.AddWithValue("origin_kind", originKind);
        command.Parameters.AddWithValue("is_active", seed.IsActive);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// RT7 — version 1 exists from the moment the tenant does, so an effective
    /// policy always resolves. Seeded FROM the release baseline rather than
    /// with a copied number, so a new tenant starts at exactly the floor it is
    /// subsequently evaluated against (RT8).
    /// </summary>
    private async Task InsertRetentionVersionOneAsync(
        DateTimeOffset provisionedAt,
        UserId createdBy,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_retention_policy
                (id, policy_version, effective_from, minimum_retention_months,
                 reason, created_at, created_by)
            VALUES
                (@id, 1, @effective_from, @months,
                 'Provisioning baseline', @created_at, @created_by)
            """,
            _connection,
            _transaction);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("effective_from", provisionedAt);
        command.Parameters.AddWithValue("months", AuditReleaseBaseline.MinimumRetentionMonths);
        command.Parameters.AddWithValue("created_at", provisionedAt);
        command.Parameters.AddWithValue("created_by", createdBy.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
