using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The classification map, held against reality.
///
/// AddUserManagementImmutabilityTriggers writes its eleven guards out by hand,
/// deliberately: invariant infrastructure should be readable without first
/// understanding a generator. The cost of that choice is that eleven hand-
/// written ROW() lists can drift — from the frozen model, from each other, and
/// from a schema that gains a column later.
///
/// This is what pays that cost. The map below is the single authoritative
/// statement of the frozen model's Immutable and Write-once classifications
/// for the eleven User Management tables, and these tests hold it against
/// BOTH the live schema and the deployed function bodies:
///
///     1. every column the schema has is classified here — a new column
///        cannot silently escape G4
///     2. every column classified here still exists
///     3. every immutable column is actually named in its table's guard
///     4. every guard exists and is ENABLE ALWAYS
///
/// Test 3 is the one that catches the hand-written mistake: a column left out
/// of a ROW() list is invisible to every other test in the suite except the
/// one attack that targets it, and an attack accidentally omitted is invisible
/// to all of them.
/// </summary>
public sealed class UserManagementImmutabilityDriftTests : IAsyncLifetime
{
    private AuditBoundaryDatabase _database = null!;

    public async Task InitializeAsync()
        => _database = await AuditBoundaryDatabase.CreateAsync();

    public async Task DisposeAsync()
        => await _database.DisposeAsync();

    /// <summary>
    /// Immutable columns per table. 72 across eleven tables, transcribed from
    /// the frozen conceptual model v2.1 entity workbook.
    /// </summary>
    private static readonly Dictionary<string, string[]> Immutable = new(StringComparer.Ordinal)
    {
        ["app_user"] = ["id", "actor_type", "created_at", "created_by"],
        ["user_identity"] =
            ["id", "user_id", "actor_type", "identity_type", "identity_provider",
             "subject_id", "created_at", "created_by"],
        ["credential"] =
            ["id", "user_identity_id", "identity_type", "created_at", "created_by"],
        ["password_history"] =
            ["id", "user_identity_id", "password_hash", "password_algorithm", "created_at"],
        ["user_token"] =
            ["id", "user_identity_id", "token_type", "token_hash", "expires_at",
             "created_at", "created_by"],
        ["user_session"] =
            ["id", "user_identity_id", "created_at", "expires_at", "ip_address", "user_agent"],
        ["security_policy"] =
            ["id", "policy_version", "effective_from", "created_at", "created_by",
             "activation_token_lifetime", "lockout_duration", "max_failed_login_attempts",
             "password_history_depth", "password_min_length", "password_reset_token_lifetime",
             "session_absolute_timeout", "session_idle_timeout"],
        ["role"] = ["id", "code", "is_system_role", "created_at", "created_by"],
        ["permission"] = ["id", "code", "created_at", "created_by"],
        ["role_permission"] =
            ["id", "role_id", "permission_id", "granted_at", "granted_by"],
        ["user_role"] =
            ["id", "user_id", "actor_type", "role_id", "scope_type", "scope_id",
             "effective_from", "assigned_at", "assigned_by", "assignment_reason"],
    };

    /// <summary>Write-once columns per table. 11 across four tables.</summary>
    private static readonly Dictionary<string, string[]> WriteOnce = new(StringComparer.Ordinal)
    {
        ["user_token"] = ["used_at", "invalidated_at"],
        ["user_session"] = ["revoked_at", "revoked_by", "revocation_reason"],
        ["role_permission"] = ["revoked_at", "revoked_by"],
        ["user_role"] = ["effective_to", "revoked_at", "revoked_by", "revocation_reason"],
    };

    /// <summary>
    /// Columns G4 does not govern, by class. Listed rather than inferred, so
    /// that adding a column to a table forces a classification decision here
    /// instead of defaulting to "unprotected".
    /// </summary>
    private static readonly Dictionary<string, string[]> NotGovernedByG4 = new(StringComparer.Ordinal)
    {
        // Mutable, Lifecycle-controlled and System-managed
        ["app_user"] =
            ["first_name", "last_name", "display_name", "email", "status",
             "deactivated_at", "deactivated_by", "updated_at", "updated_by"],
        ["user_identity"] = ["username", "status", "deactivated_at", "deactivated_by"],
        ["credential"] =
            ["password_hash", "password_algorithm", "password_changed_at",
             "must_change_password", "failed_attempt_count", "locked_until"],
        ["password_history"] = [],
        ["user_token"] = [],
        ["user_session"] = ["last_activity_at"],
        ["security_policy"] = [],
        ["role"] = ["name", "description", "is_active", "updated_at", "updated_by"],
        // Release-controlled: changed by a release migration, never a tenant.
        // That is PE2, a privilege rule, and it is not closed by G4.
        ["permission"] =
            ["name", "description", "resource", "action", "requires_human_actor", "is_active"],
        ["role_permission"] = [],
        ["user_role"] = [],
    };

    /// <summary>
    /// The two tables whose guards refuse every UPDATE outright. Their columns
    /// are all Immutable, so they appear in the map above, but their functions
    /// name no column and so are exempt from the ROW() wiring check.
    /// </summary>
    private static readonly string[] BlanketRefusal = ["password_history", "security_policy"];

    private static IEnumerable<string> Tables => Immutable.Keys;

    // ------------------------------------------------------------------

    [Fact]
    public async Task Every_column_of_every_table_is_classified()
    {
        var unclassified = new List<string>();

        foreach (var table in Tables)
        {
            var classified = Immutable[table]
                .Concat(WriteOnce.GetValueOrDefault(table, []))
                .Concat(NotGovernedByG4[table])
                .ToHashSet(StringComparer.Ordinal);

            foreach (var column in await ColumnsOfAsync(table))
                if (!classified.Contains(column))
                    unclassified.Add($"{table}.{column}");
        }

        // A new column that nobody classified is the failure mode this whole
        // file exists for: it would be unprotected, and silently so.
        Assert.Empty(unclassified);
    }

    [Fact]
    public async Task Every_classified_column_still_exists()
    {
        var missing = new List<string>();

        foreach (var table in Tables)
        {
            var actual = await ColumnsOfAsync(table);

            foreach (var column in Immutable[table]
                         .Concat(WriteOnce.GetValueOrDefault(table, []))
                         .Concat(NotGovernedByG4[table]))
                if (!actual.Contains(column))
                    missing.Add($"{table}.{column}");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Every_immutable_column_is_named_in_its_guard()
    {
        var unguarded = new List<string>();

        foreach (var table in Tables.Except(BlanketRefusal, StringComparer.Ordinal))
        {
            var body = await FunctionBodyAsync($"{table}_update_guard");

            foreach (var column in Immutable[table])
            {
                // Quoted identifiers, and BOTH sides. A bare substring match
                // would let "id" be satisfied by "user_id", and a one-sided
                // match would accept a column named only in an error message.
                if (!body.Contains($"OLD.\"{column}\"", StringComparison.Ordinal)
                    || !body.Contains($"NEW.\"{column}\"", StringComparison.Ordinal))
                    unguarded.Add($"{table}.{column}");
            }
        }

        Assert.Empty(unguarded);
    }

    [Fact]
    public async Task Every_write_once_column_is_named_in_its_guard()
    {
        var unguarded = new List<string>();

        foreach (var (table, columns) in WriteOnce)
        {
            var body = await FunctionBodyAsync($"{table}_update_guard");

            foreach (var column in columns)
                if (!body.Contains($"OLD.\"{column}\"", StringComparison.Ordinal)
                    || !body.Contains($"NEW.\"{column}\"", StringComparison.Ordinal))
                    unguarded.Add($"{table}.{column}");
        }

        Assert.Empty(unguarded);
    }

    [Fact]
    public async Task The_blanket_guards_name_no_column_and_raise_unconditionally()
    {
        foreach (var table in BlanketRefusal)
        {
            var body = await FunctionBodyAsync($"{table}_update_guard");

            // No conditional at all: the refusal cannot be reached around.
            Assert.DoesNotContain("IF ", body, StringComparison.Ordinal);
            Assert.Contains("RAISE EXCEPTION", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Every_table_has_a_guard_and_every_guard_is_enable_always()
    {
        var deployed = new Dictionary<string, char>(StringComparer.Ordinal);

        await using var connection = new NpgsqlConnection(_database.PrivilegedConnection);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname, t.tgenabled
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE NOT t.tgisinternal
              AND n.nspname = 'public'
              AND t.tgname = 'tg_' || c.relname || '_update_guard'
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            deployed[reader.GetString(0)] = reader.GetChar(1);

        foreach (var table in Tables)
        {
            Assert.True(deployed.ContainsKey(table), $"no update guard on {table}");

            // 'A' is ENABLE ALWAYS. 'O', the default, would not fire under
            // session_replication_role = replica.
            Assert.True(
                deployed[table] == 'A',
                $"trigger on {table} is '{deployed[table]}', not ENABLE ALWAYS");
        }
    }

    // ------------------------------------------------------------------

    private async Task<HashSet<string>> ColumnsOfAsync(string table)
    {
        await using var connection = new NpgsqlConnection(_database.PrivilegedConnection);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table
            """, connection);

        command.Parameters.AddWithValue("table", table);

        var columns = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));

        Assert.NotEmpty(columns);

        return columns;
    }

    private async Task<string> FunctionBodyAsync(string function)
    {
        await using var connection = new NpgsqlConnection(_database.PrivilegedConnection);
        await connection.OpenAsync();

        // The deployed definition, not the migration source: what is running
        // is the only thing that protects anything.
        await using var command = new NpgsqlCommand(
            "SELECT pg_get_functiondef(('public.' || @name || '()')::regprocedure)",
            connection);

        command.Parameters.AddWithValue("name", function);

        return (string)(await command.ExecuteScalarAsync())!;
    }
}
