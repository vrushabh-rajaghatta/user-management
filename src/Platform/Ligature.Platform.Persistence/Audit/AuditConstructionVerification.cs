using Npgsql;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// "Did Ligature.AuditSchema build what it claims to build?"
///
/// Run by the deployer after every script has been applied, on the same
/// privileged connection. It checks the structural facts the tamper boundary
/// rests on — ownership, reachability, trigger mode, the privilege matrix —
/// and fails the deployment if any is false. These are the structural
/// assertions from AuditTamperBoundaryTests, lifted into the tool that
/// produces the thing they assert about, so that a deployment stops rather
/// than hands a database on to the migrator and provisioner with a boundary
/// that only looks correct.
///
/// Deliberately NOT the adversarial suite. The tests that attempt UPDATE,
/// DELETE and DROP as each role remain tests; this verifies configuration
/// that the tests have proved sufficient, and reports every failing fact at
/// once rather than the first.
///
/// Distinct from <see cref="AuditHandoverVerification"/>, which provisioning
/// runs later to answer a different question — "is this tenant safe to hand
/// over?" — with a smaller, handover-specific set of checks. The redundancy
/// is intentional: configuration assertions are not evidence, and each
/// boundary needs its own.
/// </summary>
internal static class AuditConstructionVerification
{
    private static readonly string[] Tables =
    [
        "audit_record",
        "audit_entity_ref",
        "audit_event_type",
        "audit_event_origin",
        "audit_retention_policy",
        "audit_schema_version",
    ];

    /// <summary>Every protection trigger, each of which must be ENABLE ALWAYS.</summary>
    private static readonly (string Table, string Trigger)[] Triggers =
    [
        ("audit_record", "tg_audit_record_guard"),
        ("audit_record", "tg_audit_record_no_delete"),
        ("audit_record", "tg_audit_record_anon_ref"),
        ("audit_entity_ref", "tg_audit_entity_ref_no_delete"),
        ("audit_entity_ref", "tg_audit_entity_ref_guard"),
        ("audit_retention_policy", "tg_audit_retention_policy_frozen"),
    ];

    private static readonly string[] Indexes =
    [
        "ix_audit_record_ar22_actor",
        "ix_audit_record_ar22_entity",
        "ix_audit_record_ar22_operation",
        "ix_audit_record_ar22_causation",
        "ix_audit_record_ar22_event_type",
        "ix_audit_record_ar22_occurred_at",
        "ix_audit_record_ar22_platform_access",
        "ix_audit_entity_ref_ae6_entity",
    ];

    /// <summary>The constraints the boundary and the catalogue contract depend on.</summary>
    private static readonly (string Table, string Constraint)[] Constraints =
    [
        ("audit_record", "uq_audit_record_ar2_sequence"),
        ("audit_record", "fk_audit_record_ar4_event_type"),
        ("audit_record", "fk_audit_record_ar5_event_origin"),
        ("audit_record", "fk_audit_record_ar10_actor"),
        ("audit_record", "fk_audit_record_ar18_anonymisation"),
        ("audit_record", "ck_audit_record_ar24_snapshot_group"),
        ("audit_record", "ck_audit_record_ar27_inspected_operator_only"),
        ("audit_entity_ref", "pk_audit_entity_ref_ae2"),
        ("audit_event_origin", "fk_audit_event_origin_eo1_event_type"),
        ("audit_retention_policy", "fk_audit_retention_policy_rt5_created_by"),
    ];

    /// <summary>
    /// The privilege matrix (Design Specification section 13.2, plus the
    /// provisioning_role read access recorded in docs/architecture.md
    /// section 19). Every row is asserted, true and false alike: a grant
    /// that should not exist is as much a defect as one that is missing.
    /// </summary>
    private static readonly (string Role, string Table, string Privilege, bool Expected)[] Matrix =
    [
        // app_role — append and read
        ("app_role", "audit_record", "SELECT", true),
        ("app_role", "audit_record", "INSERT", true),
        ("app_role", "audit_record", "UPDATE", false),
        ("app_role", "audit_record", "DELETE", false),
        ("app_role", "audit_entity_ref", "INSERT", true),
        ("app_role", "audit_entity_ref", "UPDATE", false),
        ("app_role", "audit_entity_ref", "DELETE", false),
        ("app_role", "audit_event_type", "SELECT", true),
        ("app_role", "audit_event_type", "INSERT", false),
        ("app_role", "audit_event_origin", "INSERT", false),
        ("app_role", "audit_retention_policy", "INSERT", true),
        ("app_role", "audit_retention_policy", "UPDATE", false),
        ("app_role", "audit_retention_policy", "DELETE", false),

        // migration_role — nothing at all on the trail
        ("migration_role", "audit_record", "SELECT", false),
        ("migration_role", "audit_record", "INSERT", false),
        ("migration_role", "audit_record", "UPDATE", false),
        ("migration_role", "audit_record", "DELETE", false),
        ("migration_role", "audit_entity_ref", "DELETE", false),
        ("migration_role", "audit_event_type", "INSERT", true),
        ("migration_role", "audit_event_type", "UPDATE", true),
        ("migration_role", "audit_event_type", "DELETE", false),
        ("migration_role", "audit_event_origin", "DELETE", false),

        // provisioning_role — seeds the catalogue, reads the trail (003)
        ("provisioning_role", "audit_record", "SELECT", true),
        ("provisioning_role", "audit_record", "INSERT", false),
        ("provisioning_role", "audit_record", "UPDATE", false),
        ("provisioning_role", "audit_record", "DELETE", false),
        ("provisioning_role", "audit_event_type", "INSERT", true),
        ("provisioning_role", "audit_event_origin", "INSERT", true),
        ("provisioning_role", "audit_retention_policy", "INSERT", true),
        ("provisioning_role", "audit_schema_version", "SELECT", true),

        // audit_anonymiser — the AR20 columns are checked separately below
        ("audit_anonymiser", "audit_record", "SELECT", true),
        ("audit_anonymiser", "audit_record", "INSERT", true),
        ("audit_anonymiser", "audit_record", "DELETE", false),
        ("audit_anonymiser", "audit_entity_ref", "UPDATE", false),
        ("audit_anonymiser", "audit_entity_ref", "DELETE", false),
    ];

    private static readonly string[] Ar20Columns =
    [
        "actor_display_name", "actor_username", "actor_email",
        "before", "after", "payload", "anonymisation_audit_id",
    ];

    /// <summary>A sample of columns OUTSIDE the AR20 set; the anonymiser must hold UPDATE on none.</summary>
    private static readonly string[] ImmutableColumnsSample =
    [
        "audit_id", "sequence", "occurred_at", "event_type", "reason",
        "actor_user_id", "actor_type", "actor_subject_id", "operation_id",
    ];

    public static async Task VerifyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        await CheckOwnershipAsync(connection, failures, cancellationToken);
        await CheckOwnerRoleAsync(connection, failures, cancellationToken);
        await CheckTablesAsync(connection, failures, cancellationToken);
        await CheckTriggersAsync(connection, failures, cancellationToken);
        await CheckIndexesAndConstraintsAsync(connection, failures, cancellationToken);
        await CheckPrivilegesAsync(connection, failures, cancellationToken);

        if (failures.Count > 0)
        {
            throw new AuditSchemaDeploymentException(
                "The Audit schema was applied but does not verify. The "
                + "deployment stops here rather than handing on a boundary "
                + "that is not the one it claims:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures.Select(x => "  - " + x)));
        }
    }

    private static async Task CheckOwnershipAsync(
        NpgsqlConnection connection,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var schemaOwner = await ScalarAsync<string?>(connection,
            "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = 'audit'",
            cancellationToken);

        if (schemaOwner != AuditSchemaDeployer.OwnerRole)
            failures.Add($"schema 'audit' is owned by '{schemaOwner ?? "<missing>"}', not {AuditSchemaDeployer.OwnerRole}");

        // DROP TABLE permits the table owner, the SCHEMA owner, or a
        // superuser. Every object must therefore be audit_owner's, not
        // merely the schema.
        var foreign = await ScalarAsync<string?>(connection,
            """
            SELECT string_agg(c.relname || '=' || pg_get_userbyid(c.relowner), ', ' ORDER BY c.relname)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'audit' AND c.relkind IN ('r', 'i', 'S')
              AND pg_get_userbyid(c.relowner) <> 'audit_owner'
            """, cancellationToken);

        if (foreign is not null)
            failures.Add($"audit objects not owned by audit_owner: {foreign}");

        var functions = await ScalarAsync<string?>(connection,
            """
            SELECT string_agg(p.proname || '=' || pg_get_userbyid(p.proowner), ', ')
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'audit' AND pg_get_userbyid(p.proowner) <> 'audit_owner'
            """, cancellationToken);

        if (functions is not null)
            failures.Add($"audit functions not owned by audit_owner: {functions}");
    }

    /// <summary>
    /// The owner's authority is implicit and unrevokable, so the boundary
    /// rests on nobody being able to become it: NOLOGIN closes the front
    /// door, zero members closes SET ROLE.
    /// </summary>
    private static async Task CheckOwnerRoleAsync(
        NpgsqlConnection connection,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var state = await ScalarAsync<string?>(connection,
            """
            SELECT rolcanlogin::text || ',' || rolsuper::text || ',' ||
                   (SELECT count(*) FROM pg_auth_members m WHERE m.roleid = r.oid)::text
            FROM pg_roles r WHERE rolname = 'audit_owner'
            """, cancellationToken);

        if (state is null)
        {
            failures.Add("role audit_owner does not exist");
            return;
        }

        var parts = state.Split(',');

        if (parts[0] != "false") failures.Add("audit_owner can log in");
        if (parts[1] != "false") failures.Add("audit_owner is a superuser");
        if (parts[2] != "0") failures.Add($"audit_owner has {parts[2]} member(s); it must have none");
    }

    private static async Task CheckTablesAsync(
        NpgsqlConnection connection,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        foreach (var table in Tables)
        {
            var exists = await ScalarAsync<bool>(connection,
                $"SELECT to_regclass('audit.{table}') IS NOT NULL", cancellationToken);

            if (!exists) failures.Add($"table audit.{table} is missing");
        }

        // AR6 — origin_kind must be the generated column, not an ordinary one
        // something could write.
        var generated = await ScalarAsync<bool>(connection,
            """
            SELECT coalesce(bool_or(attgenerated = 's'), false)
            FROM pg_attribute WHERE attrelid = 'audit.audit_record'::regclass AND attname = 'origin_kind'
            """, cancellationToken);

        if (!generated) failures.Add("audit_record.origin_kind is not a stored generated column (AR6)");
    }

    private static async Task CheckTriggersAsync(
        NpgsqlConnection connection,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        foreach (var (table, trigger) in Triggers)
        {
            // 'A' is ENABLE ALWAYS. 'O', the default, would not fire under
            // session_replication_role = replica.
            var mode = await ScalarAsync<string?>(connection,
                $"""
                SELECT tgenabled::text FROM pg_trigger
                WHERE tgrelid = 'audit.{table}'::regclass AND tgname = '{trigger}'
                """, cancellationToken);

            if (mode is null) failures.Add($"trigger {trigger} on audit.{table} is missing");
            else if (mode != "A") failures.Add($"trigger {trigger} on audit.{table} is '{mode}', not ENABLE ALWAYS");
        }
    }

    private static async Task CheckIndexesAndConstraintsAsync(
        NpgsqlConnection connection,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        foreach (var index in Indexes)
        {
            var exists = await ScalarAsync<bool>(connection,
                $"SELECT to_regclass('audit.{index}') IS NOT NULL", cancellationToken);

            if (!exists) failures.Add($"index {index} is missing");
        }

        foreach (var (table, constraint) in Constraints)
        {
            var exists = await ScalarAsync<bool>(connection,
                $"""
                SELECT EXISTS (SELECT 1 FROM pg_constraint
                               WHERE conrelid = 'audit.{table}'::regclass AND conname = '{constraint}')
                """, cancellationToken);

            if (!exists) failures.Add($"constraint {constraint} on audit.{table} is missing");
        }
    }

    private static async Task CheckPrivilegesAsync(
        NpgsqlConnection connection,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        foreach (var (role, table, privilege, expected) in Matrix)
        {
            var actual = await ScalarAsync<bool>(connection,
                $"SELECT has_table_privilege('{role}', 'audit.{table}', '{privilege}')",
                cancellationToken);

            if (actual != expected)
            {
                failures.Add(expected
                    ? $"{role} lacks {privilege} on audit.{table}"
                    : $"{role} holds {privilege} on audit.{table}, and must not");
            }
        }

        // AR20 — the anonymiser's UPDATE is column-level and exactly this set.
        foreach (var column in Ar20Columns)
        {
            var held = await ScalarAsync<bool>(connection,
                $"SELECT has_column_privilege('audit_anonymiser', 'audit.audit_record', '{column}', 'UPDATE')",
                cancellationToken);

            if (!held) failures.Add($"audit_anonymiser lacks UPDATE on audit_record.{column} (AR20)");
        }

        foreach (var column in ImmutableColumnsSample)
        {
            var held = await ScalarAsync<bool>(connection,
                $"SELECT has_column_privilege('audit_anonymiser', 'audit.audit_record', '{column}', 'UPDATE')",
                cancellationToken);

            if (held) failures.Add($"audit_anonymiser holds UPDATE on audit_record.{column}, outside the AR20 set");
        }

        // AG1 — no reachable role holds DELETE on the trail. audit_owner is
        // excluded because an owner's privileges are implicit and cannot be
        // revoked; what makes that safe is CheckOwnerRoleAsync.
        var deleters = await ScalarAsync<string?>(connection,
            """
            SELECT string_agg(DISTINCT r.rolname, ', ')
            FROM pg_roles r
            WHERE NOT r.rolsuper AND r.rolname NOT LIKE 'pg\_%' AND r.rolname <> 'audit_owner'
              AND (has_table_privilege(r.rolname, 'audit.audit_record', 'DELETE')
                OR has_table_privilege(r.rolname, 'audit.audit_entity_ref', 'DELETE'))
            """, cancellationToken);

        if (deleters is not null) failures.Add($"DELETE on the trail is held by: {deleters}");
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is null or DBNull ? default! : (T)value;
    }
}
