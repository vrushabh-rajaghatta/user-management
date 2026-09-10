using System.Security.Cryptography;
using System.Text;
using Ligature.Platform.Domain.Users;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// A caller that is seeded once and never removed.
///
/// This exists because of what the audit trail is. A record names its actor
/// through a foreign key to app_user (AR10) and the assignment that
/// authorised the action through another to user_role (AR12), and audit rows
/// cannot be deleted by anyone — the guard trigger is ENABLE ALWAYS, so not
/// by the application, not by the schema owner, not by a superuser. The
/// moment a caller acts, the rows the trail points at are permanent, and the
/// per-test caller these suites used to seed and delete became undeletable
/// halfway through its own test.
///
/// The resolution is not to weaken the constraints; they are the reason an
/// actor recorded in the trail can still be identified years later. It is to
/// stop creating callers that must be thrown away. Every identifier here is
/// derived from the role the caller holds, so a run reuses what the previous
/// run seeded: the shared database gains one caller per role, once, rather
/// than one per test.
///
/// The users these tests CREATE are still deleted: a created user is the
/// subject of a record, not its actor, and no foreign key points at it.
/// </summary>
internal static class PermanentTestCaller
{
    private static readonly DateTimeOffset Seeded =
        new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Ensures the caller for this role exists, and returns it. Idempotent:
    /// on the second and later runs every statement is a no-op.
    ///
    /// Distinct per role: a caller holding access-reviewer must not be the
    /// caller holding user-administrator, or a test proving that the pipeline
    /// checks the SPECIFIC permission would prove nothing.
    /// </summary>
    /// <param name="roleCode">
    /// The seeded role the caller holds, or null for a caller holding none.
    /// </param>
    internal static Task<PermanentCaller> EnsureAsync(
        string connectionString, string? roleCode)
        => EnsureAsync(connectionString, roleCode ?? "unprivileged", roleCode);

    /// <summary>
    /// The same, for callers a test distinguishes by purpose rather than by
    /// role — two role-less callers where one must not be the other.
    /// </summary>
    internal static async Task<PermanentCaller> EnsureAsync(
        string connectionString, string label, string? roleCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var userId = new UserId(Derive("user", label));
        var identityId = Derive("identity", label);
        var assignmentId = Derive("assignment", label);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        await using (var user = new NpgsqlCommand(
            """
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email,
                 status, created_at, created_by, updated_at, updated_by)
            VALUES
                (@id, 'Human', 'Permanent', 'Caller', @displayName, @email,
                 'Active', @now, @system, @now, @system)
            ON CONFLICT (id) DO NOTHING
            """, connection, transaction))
        {
            user.Parameters.AddWithValue("id", userId.Value);
            user.Parameters.AddWithValue("displayName", $"Permanent {label}");
            user.Parameters.AddWithValue("email", $"permanent-{label}@example.test");
            user.Parameters.AddWithValue("now", Seeded);
            user.Parameters.AddWithValue("system", User.SystemUserId.Value);
            await user.ExecuteNonQueryAsync();
        }

        await using (var identity = new NpgsqlCommand(
            """
            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, created_at, created_by)
            VALUES
                (@id, @userId, 'Human', 'Local', 'Application',
                 @subjectId, @username, 'Active', @now, @system)
            ON CONFLICT (id) DO NOTHING
            """, connection, transaction))
        {
            identity.Parameters.AddWithValue("id", identityId);
            identity.Parameters.AddWithValue("userId", userId.Value);
            identity.Parameters.AddWithValue("subjectId", identityId.ToString());
            identity.Parameters.AddWithValue("username", $"permanent-{label}");
            identity.Parameters.AddWithValue("now", Seeded);
            identity.Parameters.AddWithValue("system", User.SystemUserId.Value);
            await identity.ExecuteNonQueryAsync();
        }

        if (roleCode is not null)
        {
            await using (var assignment = new NpgsqlCommand(
                """
                INSERT INTO user_role
                    (id, user_id, actor_type, role_id, scope_type, scope_id,
                     effective_from, effective_to, assigned_at, assigned_by,
                     assignment_reason)
                SELECT @id, @userId, 'Human', r.id, 'Global', NULL,
                       @from, NULL, @now, @system, 'USR-C1 integration tests.'
                FROM role r
                WHERE r.code = @roleCode
                ON CONFLICT (id) DO NOTHING
                """, connection, transaction))
            {
                assignment.Parameters.AddWithValue("id", assignmentId);
                assignment.Parameters.AddWithValue("userId", userId.Value);
                assignment.Parameters.AddWithValue("roleCode", roleCode);
                assignment.Parameters.AddWithValue("from", Seeded.AddDays(-1));
                assignment.Parameters.AddWithValue("now", Seeded);
                assignment.Parameters.AddWithValue("system", User.SystemUserId.Value);
                await assignment.ExecuteNonQueryAsync();
            }

            await using var held = new NpgsqlCommand(
                "SELECT count(*) FROM user_role WHERE id = @id",
                connection, transaction);

            held.Parameters.AddWithValue("id", assignmentId);

            Assert.True(
                Convert.ToInt32(await held.ExecuteScalarAsync()) == 1,
                $"The seeded role '{roleCode}' is missing; PRV-C1 creates it.");
        }

        await transaction.CommitAsync();

        return new PermanentCaller(userId, new UserIdentityId(identityId));
    }

    /// <summary>
    /// A stable identifier for (kind, label). Derived rather than listed, so
    /// a test using a role no caller has used before still gets its own
    /// permanent caller instead of silently borrowing another role's.
    /// </summary>
    private static Guid Derive(string kind, string label)
    {
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes($"ligature-permanent-test-caller:{kind}:{label}"));

        var bytes = digest[..16];

        // Version 8 (custom) and the RFC 9562 variant bits, so the value is a
        // well-formed UUID rather than 16 arbitrary bytes.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}

/// <summary>
/// A caller that outlives the test. Both identifiers are stable across runs,
/// which is the point: the rows an audit record refers to cannot be removed,
/// so they are reused rather than recreated.
/// </summary>
internal sealed record PermanentCaller(UserId UserId, UserIdentityId IdentityId);
