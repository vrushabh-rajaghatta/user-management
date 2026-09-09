using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Provisioning;
using Npgsql;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// "Is this tenant safe to hand over?"
///
/// AUD-C4 steps 1-2 before the seed and AUD-S11 after it, run by
/// provisioning on its own transaction. Provisioning does not trust that
/// Ligature.AuditSchema succeeded because it ran earlier; it establishes the
/// handover facts itself and refuses — rolling the whole provisioning back —
/// if any is false. A tenant is never handed over with a writable trail.
///
/// Small and deterministic, tied to the handover acceptance criteria and
/// nothing more. The deep structural checks belong to
/// <see cref="AuditConstructionVerification"/> in the tool that built the
/// schema; the adversarial ones stay in the test suite. What provisioning
/// asks is narrower: can the application rewrite the trail, is the
/// catalogue what the release says, does a retention floor exist, and is the
/// trail in the state its first record expects to find it.
///
/// Runs as provisioning_role, which can read the trail and the ledger (003)
/// but hold nothing else on them, so has_table_privilege — which answers for
/// ANY role — is the mechanism, not an attempted write.
/// </summary>
internal static class AuditHandoverVerification
{
    /// <summary>
    /// AUD-C4 steps 1 and 2: the structure and privileges the seed is about
    /// to rely on. Checked BEFORE seeding so a failure costs nothing to roll
    /// back.
    /// </summary>
    public static async Task VerifyStructureAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        // No ledger means no deployment at all — the most likely way for a
        // tenant to arrive here wrong, and it must read as a refusal with a
        // remedy, not as a missing-table error from the first probe below.
        var ledgerExists = await ScalarAsync<bool>(connection, transaction,
            "SELECT to_regclass('audit.audit_schema_version') IS NOT NULL",
            cancellationToken);

        if (!ledgerExists)
        {
            Refuse(
                ["the Audit schema is not deployed; run Ligature.AuditSchema after the migrations"],
                "before seeding");
        }

        // The deployer's ledger, which records every script applied.
        // Missing rows mean the schema this seed targets is not the one the
        // release was built against.
        foreach (var script in AuditSchemaDeployer.ExpectedScriptNames)
        {
            var applied = await ScalarAsync<bool>(connection, transaction,
                "SELECT EXISTS (SELECT 1 FROM audit.audit_schema_version WHERE script_name = @name)",
                cancellationToken, ("name", script));

            if (!applied) failures.Add($"audit schema script {script} has not been applied");
        }

        // The property AUD-C4 names explicitly: the application role holds no
        // UPDATE or DELETE on an audit table, and the anonymiser nothing
        // beyond the AR20 set.
        foreach (var (table, privilege) in new[]
        {
            ("audit_record", "UPDATE"), ("audit_record", "DELETE"),
            ("audit_entity_ref", "UPDATE"), ("audit_entity_ref", "DELETE"),
            ("audit_event_type", "UPDATE"), ("audit_event_type", "DELETE"),
            ("audit_event_origin", "UPDATE"), ("audit_event_origin", "DELETE"),
            ("audit_retention_policy", "UPDATE"), ("audit_retention_policy", "DELETE"),
        })
        {
            var held = await ScalarAsync<bool>(connection, transaction,
                $"SELECT has_table_privilege('app_role', 'audit.{table}', '{privilege}')",
                cancellationToken);

            if (held) failures.Add($"the application role holds {privilege} on audit.{table}");
        }

        foreach (var column in new[] { "audit_id", "sequence", "event_type", "reason", "actor_user_id", "operation_id" })
        {
            var held = await ScalarAsync<bool>(connection, transaction,
                $"SELECT has_column_privilege('audit_anonymiser', 'audit.audit_record', '{column}', 'UPDATE')",
                cancellationToken);

            if (held) failures.Add($"the anonymiser role holds UPDATE on audit_record.{column}, outside the AR20 set");
        }

        // AUD-S10 — the tenant's first record must be Sequence 1, and it is
        // emitted later in this transaction. So the trail must be empty NOW
        // and its sequence never consumed: a rolled-back write still consumes
        // a value (AUD-D32), and a tenant whose first record would land at
        // Sequence 2 is refused here, where the cause is nameable, rather
        // than by the probe after emission.
        var records = await ScalarAsync<long>(connection, transaction,
            "SELECT count(*) FROM audit.audit_record", cancellationToken);

        if (records != 0)
            failures.Add($"the trail already holds {records} record(s); a tenant is provisioned once, onto an empty trail");

        var consumed = await ScalarAsync<bool>(connection, transaction,
            "SELECT is_called FROM audit.audit_record_sequence_seq", cancellationToken);

        if (consumed)
            failures.Add("the audit sequence has already been consumed, so the first record cannot be Sequence 1 (AUD-S10)");

        Refuse(failures, "before seeding");
    }

    /// <summary>
    /// AUD-S11 — the handover probes, after the seed and after the tenant's
    /// first record has been emitted: the catalogue matches the release, EO5
    /// holds exactly, retention v1 exists, and the trail holds exactly one
    /// record — TenantProvisioned, Sequence 1, by the System actor.
    /// </summary>
    public static async Task VerifyHandoverAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        var expectedTypes = AuditEventCatalogue.GetEventTypeSeeds().Count;
        var actualTypes = await ScalarAsync<long>(connection, transaction,
            "SELECT count(*) FROM audit.audit_event_type", cancellationToken);

        if (actualTypes != expectedTypes)
            failures.Add($"catalogue has {actualTypes} event type(s); the release seed has {expectedTypes}");

        // EO5 — Anonymous is declared only where the model says, and the
        // model says twice.
        var anonymous = await ScalarAsync<string?>(connection, transaction,
            "SELECT string_agg(code, ',' ORDER BY code) FROM audit.audit_event_origin WHERE origin_kind = 'Anonymous'",
            cancellationToken);

        var expectedAnonymous = string.Join(",", AuditEventCatalogue.AnonymousOriginCodes.Order(StringComparer.Ordinal));

        if (anonymous != expectedAnonymous)
            failures.Add($"Anonymous origin is declared for [{anonymous}]; EO5 permits exactly [{expectedAnonymous}]");

        var retention = await ScalarAsync<long>(connection, transaction,
            "SELECT count(*) FROM audit.audit_retention_policy WHERE policy_version = 1",
            cancellationToken);

        if (retention != 1) failures.Add("retention policy version 1 is not present (RT7)");

        // AUD-S10 / AUD-S11 — exactly one record, Sequence 1, TenantProvisioned,
        // by the System actor. The handover fact, in its final form.
        var first = await ScalarAsync<string?>(connection, transaction,
            "SELECT (SELECT count(*) FROM audit.audit_record)::text || '|' || "
            + "coalesce((SELECT sequence::text || '|' || event_type || '|' || coalesce(actor_user_id::text, '') "
            + "FROM audit.audit_record ORDER BY sequence LIMIT 1), '')",
            cancellationToken);

        var expectedFirst = $"1|1|TenantProvisioned|{User.SystemUserId.Value}";

        if (first != expectedFirst)
            failures.Add($"the trail's first record is [{first}]; it must be exactly one record, Sequence 1, TenantProvisioned by the System actor (AUD-S10)");

        Refuse(failures, "at handover");
    }

    private static void Refuse(List<string> failures, string stage)
    {
        if (failures.Count == 0) return;

        throw new ProvisioningException(
            $"Audit verification failed {stage}, so this tenant is not handed "
            + "over. Nothing has been provisioned:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures.Select(x => "  - " + x)));
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);

        foreach (var (name, argument) in parameters)
            command.Parameters.AddWithValue(name, argument);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is null or DBNull ? default! : (T)value;
    }
}
