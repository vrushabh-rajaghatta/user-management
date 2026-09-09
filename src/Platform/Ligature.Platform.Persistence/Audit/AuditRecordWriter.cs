using Ligature.Platform.Application.Audit;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// The single writer's implementation (IMPL-02): raw SQL into
/// audit.audit_record and audit.audit_entity_ref on a transaction the caller
/// already holds.
///
/// Raw SQL, not EF, because the audit tables are not this application's to
/// model (docs/architecture.md section 19): they belong to audit_owner, the
/// application writes into them with INSERT only, and an entity type over
/// them would present the trail as application-owned state. The same
/// mechanism AuditCatalogueSeeder uses, and for the same reason.
///
/// Two entry points. The scoped one, used by the emission behaviour, writes
/// on the DbContext's current transaction — the one behaviour 6 opened — and
/// refuses if there is none, because a write outside the command's
/// transaction is a write that could commit when the command does not
/// (AUD-4). The static one takes an explicit connection and transaction, for
/// the single emitter that is not a command: provisioning, writing
/// TenantProvisioned on its own transaction.
/// </summary>
internal sealed class AuditRecordWriter : IAuditRecordWriter
{
    private readonly LigatureDbContext _dbContext;

    public AuditRecordWriter(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task WriteAsync(
        IReadOnlyList<AuditRecordRow> rows,
        CancellationToken cancellationToken)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Audit emission defect — no transaction is open on this scope, "
                + "so the audit rows have no unit of work to commit with. The "
                + "emission behaviour runs inside TransactionScopeBehavior; a "
                + "pipeline without it cannot satisfy AUD-4.");

        await WriteAsync(
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction(),
            rows,
            cancellationToken);
    }

    /// <summary>
    /// Records first, then their references (AE8), in the order given — the
    /// assembler has already put causes before effects (AR16).
    /// </summary>
    internal static async Task WriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<AuditRecordRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        foreach (var row in rows)
        {
            await InsertRecordAsync(connection, transaction, row, cancellationToken);

            foreach (var reference in row.References)
                await InsertReferenceAsync(connection, transaction, row.AuditId, reference, cancellationToken);
        }
    }

    private static async Task InsertRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditRecordRow row,
        CancellationToken cancellationToken)
    {
        // Sequence is the identity column and created_at the database's
        // now(); neither is supplied. origin_kind is generated (AR6).
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_record
                (audit_id, occurred_at, captured_at,
                 event_type, event_version, write_path, regulatory_classification,
                 reason_required, reason,
                 actor_user_id, actor_type, actor_display_name, actor_username,
                 actor_email, actor_identity_provider, actor_subject_id,
                 authorizing_role_id, authorizing_role_name, authorizing_scope_type,
                 authorizing_scope_id, authorizing_assignment_id,
                 on_behalf_of, agent_context, platform_access_ref, actor_captured_at,
                 entity_type, entity_id, operation_id, causation_id,
                 before, after, payload)
            VALUES
                (@audit_id, @occurred_at, @captured_at,
                 @event_type, @event_version, @write_path, @classification,
                 @reason_required, @reason,
                 @actor_user_id, @actor_type, @actor_display_name, @actor_username,
                 @actor_email, @actor_identity_provider, @actor_subject_id,
                 @authorizing_role_id, @authorizing_role_name, @authorizing_scope_type,
                 @authorizing_scope_id, @authorizing_assignment_id,
                 NULL, NULL, @platform_access_ref, @actor_captured_at,
                 @entity_type, @entity_id, @operation_id, @causation_id,
                 @before, @after, @payload)
            """,
            connection,
            transaction);

        var p = command.Parameters;
        var actor = row.Actor;

        p.AddWithValue("audit_id", row.AuditId);
        p.AddWithValue("occurred_at", row.OccurredAt);
        p.AddWithValue("captured_at", row.CapturedAt);
        p.AddWithValue("event_type", row.EventType);
        p.AddWithValue("event_version", row.EventVersion);
        p.AddWithValue("write_path", row.WritePath);
        p.AddWithValue("classification", row.RegulatoryClassification);
        p.AddWithValue("reason_required", row.ReasonRequired);
        p.AddWithValue("reason", (object?)row.Reason ?? DBNull.Value);

        p.AddWithValue("actor_user_id", (object?)actor.UserId?.Value ?? DBNull.Value);
        p.AddWithValue("actor_type", (object?)actor.ActorType?.ToString() ?? DBNull.Value);
        p.AddWithValue("actor_display_name", (object?)actor.DisplayName ?? DBNull.Value);
        p.AddWithValue("actor_username", (object?)actor.Username ?? DBNull.Value);
        p.AddWithValue("actor_email", (object?)actor.Email ?? DBNull.Value);
        p.AddWithValue("actor_identity_provider", (object?)actor.IdentityProvider ?? DBNull.Value);
        p.AddWithValue("actor_subject_id", (object?)actor.SubjectId ?? DBNull.Value);
        p.AddWithValue("authorizing_role_id", (object?)actor.AuthorizingRoleId?.Value ?? DBNull.Value);
        p.AddWithValue("authorizing_role_name", (object?)actor.AuthorizingRoleName ?? DBNull.Value);
        p.AddWithValue("authorizing_scope_type", (object?)actor.AuthorizingScopeType ?? DBNull.Value);
        p.AddWithValue("authorizing_scope_id", (object?)actor.AuthorizingScopeId ?? DBNull.Value);
        p.AddWithValue("authorizing_assignment_id", (object?)actor.AuthorizingAssignmentId?.Value ?? DBNull.Value);
        p.AddWithValue("platform_access_ref", (object?)actor.PlatformAccessRef ?? DBNull.Value);
        p.AddWithValue("actor_captured_at", (object?)actor.CapturedAt ?? DBNull.Value);

        p.AddWithValue("entity_type", row.EntityType);
        p.AddWithValue("entity_id", (object?)row.EntityId ?? DBNull.Value);
        p.AddWithValue("operation_id", row.OperationId);
        p.AddWithValue("causation_id", (object?)row.CausationId ?? DBNull.Value);

        p.Add("before", NpgsqlDbType.Jsonb).Value = (object?)row.Before ?? DBNull.Value;
        p.Add("after", NpgsqlDbType.Jsonb).Value = (object?)row.After ?? DBNull.Value;
        p.Add("payload", NpgsqlDbType.Jsonb).Value = (object?)row.Payload ?? DBNull.Value;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid auditId,
        AuditEntityReference reference,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_entity_ref (audit_id, entity_type, entity_id, ref_role)
            VALUES (@audit_id, @entity_type, @entity_id, @ref_role)
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("audit_id", auditId);
        command.Parameters.AddWithValue("entity_type", reference.EntityType);
        command.Parameters.AddWithValue("entity_id", reference.EntityId);
        command.Parameters.AddWithValue("ref_role", reference.Role);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
