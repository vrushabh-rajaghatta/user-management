using System.Text.Json;
using Ligature.Platform.Application.Audit;
using Npgsql;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// Builds the in-memory catalogue (IMPL-08) from the database.
///
/// Both readers now read the SAME deployed rows. The host loads them at
/// process start and verifies its compiled declarations against them.
/// Provisioning loads them on its own transaction to validate
/// TenantProvisioned, because the catalogue is deployed by
/// Ligature.AuditSchema BEFORE either process runs.
///
/// <see cref="FromSeeds"/> survives for tests that need a snapshot with no
/// database behind it. Production paths must not use it: a snapshot built
/// from the compiled seed would agree with the handlers by construction,
/// which is precisely the disagreement IMPL-08 exists to detect.
/// </summary>
internal static class AuditEventCatalogueLoader
{
    public static AuditEventCatalogueSnapshot FromSeeds()
        => new(AuditEventCatalogue.GetEventTypeSeeds().Select(ToDefinition));

    /// <summary>
    /// Synchronous by design: it runs once, inside a Lazy, at the moment the
    /// singleton is first resolved.
    /// </summary>
    public static AuditEventCatalogueSnapshot Load(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        return Read(connection, transaction: null);
    }

    /// <summary>
    /// Reads on a connection and transaction the caller already holds, so
    /// provisioning sees the catalogue from inside its own transaction rather
    /// than opening a second connection mid-procedure.
    /// </summary>
    public static AuditEventCatalogueSnapshot Load(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        return Read(connection, transaction);
    }

    private static AuditEventCatalogueSnapshot Read(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction)
    {
        var origins = new Dictionary<(string, int), Dictionary<string, bool>>();

        using (var command = new NpgsqlCommand(
            "SELECT code, version, origin_kind, is_active FROM audit.audit_event_origin",
            connection,
            transaction))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = (reader.GetString(0), reader.GetInt32(1));

                if (!origins.TryGetValue(key, out var kinds))
                    origins[key] = kinds = new Dictionary<string, bool>(StringComparer.Ordinal);

                kinds[reader.GetString(2)] = reader.GetBoolean(3);
            }
        }

        var definitions = new List<AuditEventTypeDefinition>();

        using (var command = new NpgsqlCommand(
            """
            SELECT code, version, owning_context, default_classification,
                   reason_required, write_path, shape, primary_entity_type,
                   primary_entity_required, entity_ref_roles::text, pii_paths::text,
                   is_active
            FROM audit.audit_event_type
            """, connection, transaction))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var code = reader.GetString(0);
                var version = reader.GetInt32(1);

                definitions.Add(new AuditEventTypeDefinition(
                    code,
                    version,
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetBoolean(8),
                    (JsonSerializer.Deserialize<List<EntityRefRoleSeed>>(reader.GetString(9)) ?? [])
                        .Select(x => new AuditEntityRefRole(x.EntityType, x.RefRole, x.Required))
                        .ToList(),
                    (JsonSerializer.Deserialize<List<PiiPathSeed>>(reader.GetString(10)) ?? [])
                        .Select(x => new AuditPiiPath(x.Path, x.Describes))
                        .ToList(),
                    reader.GetBoolean(11),
                    origins.GetValueOrDefault((code, version))
                        ?? new Dictionary<string, bool>(StringComparer.Ordinal)));
            }
        }

        return new AuditEventCatalogueSnapshot(definitions);
    }

    private static AuditEventTypeDefinition ToDefinition(EventTypeSeed seed)
        => new(
            seed.Code,
            seed.Version,
            seed.OwningContext,
            seed.DefaultClassification,
            seed.ReasonRequired,
            seed.WritePath,
            seed.Shape,
            seed.PrimaryEntityType,
            seed.PrimaryEntityRequired,
            seed.EntityRefRoles.Select(x => new AuditEntityRefRole(x.EntityType, x.RefRole, x.Required)).ToList(),
            seed.PiiPaths.Select(x => new AuditPiiPath(x.Path, x.Describes)).ToList(),
            seed.IsActive,
            seed.Origins.ToDictionary(x => x, _ => seed.IsActive, StringComparer.Ordinal));
}

/// <summary>
/// The catalogue singleton: loaded on first access, once per process, and
/// never refreshed — AUD-C3 runs during deployment before the application
/// resumes, so there is no in-process invalidation to get wrong (IMPL-08).
/// The host forces that first access at start and verifies the declarations
/// against it, so a mismatch stops the process rather than the first
/// affected command.
/// </summary>
internal sealed class LazyAuditEventCatalogue : IAuditEventCatalogue
{
    private readonly Lazy<AuditEventCatalogueSnapshot> _snapshot;

    public LazyAuditEventCatalogue(string connectionString)
    {
        _snapshot = new Lazy<AuditEventCatalogueSnapshot>(
            () => AuditEventCatalogueLoader.Load(connectionString),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public AuditEventTypeDefinition? Find(string code, int version)
        => _snapshot.Value.Find(code, version);

    public IReadOnlyCollection<AuditEventTypeDefinition> All
        => _snapshot.Value.All;
}
