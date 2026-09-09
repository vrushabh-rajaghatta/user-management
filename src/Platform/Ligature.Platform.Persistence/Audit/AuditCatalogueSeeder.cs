using System.Text.Json;
using Ligature.Platform.Domain.Audit;
using Ligature.Platform.Domain.Users;
using Npgsql;
using NpgsqlTypes;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// AUD-C4 steps 3 and 4: seeds the event catalogue and retention policy v1
/// into the audit schema, on the provisioning transaction.
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
    /// Plain INSERTs, and deliberately not upserts. AUD-C4 runs once per
    /// tenant database, inside PRV-C1's System-actor sentinel, against an
    /// empty catalogue. A duplicate here means that assumption is false, and
    /// the right outcome is a loud failure that rolls the whole provisioning
    /// back — not a quiet overwrite that would make AUD-C4 behave like
    /// AUD-C3 without AUD-C3's audit event.
    /// </summary>
    public async Task SeedAsync(
        DateTimeOffset provisionedAt,
        UserId createdBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createdBy);

        foreach (var seed in AuditEventCatalogue.GetEventTypeSeeds())
        {
            await InsertEventTypeAsync(seed, cancellationToken);

            // Origins after their type: EO1 is a foreign key.
            foreach (var origin in seed.Origins)
            {
                await InsertOriginAsync(seed, origin, cancellationToken);
            }
        }

        await InsertRetentionVersionOneAsync(
            provisionedAt, createdBy, cancellationToken);
    }

    private async Task InsertEventTypeAsync(
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
    private async Task InsertOriginAsync(
        EventTypeSeed seed,
        string originKind,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_event_origin (code, version, origin_kind, is_active)
            VALUES (@code, @version, @origin_kind, @is_active)
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
