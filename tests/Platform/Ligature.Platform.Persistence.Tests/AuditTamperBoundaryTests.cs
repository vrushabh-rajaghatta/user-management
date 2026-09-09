using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// AUD-S01. The proposition:
///
///     No non-superuser role that participates in application, migration,
///     provisioning or anonymisation execution can alter or delete a
///     committed audit record.
///
/// These are adversarial tests, not configuration assertions. Each one
/// attempts the thing the boundary exists to prevent, against a database
/// built the way a customer installation is built, and requires the attempt
/// to be refused. "The grants are as expected" is not the claim; "the attack
/// fails" is.
///
/// The first green test for Audit is deliberately NOT "Audit accepts an
/// INSERT". It is that a committed row survives every attempt the running
/// system can make on it.
///
/// A cluster superuser is outside the claim, because PostgreSQL places no
/// constraint on a superuser. What the boundary does remove is the
/// session-setting route: ENABLE ALWAYS means even a superuser must issue
/// DDL against the trail rather than flipping session_replication_role.
/// </summary>
public sealed class AuditTamperBoundaryTests
{
    // ------------------------------------------------------------------
    // app_role — the credential the running application actually holds
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_application_can_append_to_the_trail()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        await SeedCatalogueAsync(database);

        var affected = await ExecuteAsync(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            InsertRecord());

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task The_application_cannot_rewrite_a_committed_record()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        await SeedCatalogueAsync(database);
        await ExecuteAsync(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            InsertRecord());

        var failure = await Refused(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            "UPDATE audit.audit_record SET reason = 'rewritten'");

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    [Fact]
    public async Task The_application_cannot_delete_a_committed_record()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        await SeedCatalogueAsync(database);
        await ExecuteAsync(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            InsertRecord());

        var failure = await Refused(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            "DELETE FROM audit.audit_record");

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);

        Assert.Equal(1, await CountRecordsAsync(database));
    }

    /// <summary>
    /// The route that makes a trigger-only defence worthless: with
    /// session_replication_role set to replica, ordinary triggers do not
    /// fire. The application may not set it at all.
    /// </summary>
    [Fact]
    public async Task The_application_cannot_disable_trigger_processing()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        var failure = await Refused(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            "SET session_replication_role = replica");

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    [Theory]
    [InlineData(AuditBoundaryDatabase.AnonymiserRole)]
    [InlineData(AuditBoundaryDatabase.OwnerRole)]
    public async Task The_application_cannot_become_a_privileged_role(string role)
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        var failure = await Refused(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            $"SET ROLE {role}");

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    // ------------------------------------------------------------------
    // migration_role — owns the database, and therefore the public schema
    // ------------------------------------------------------------------

    /// <summary>
    /// This is the case that a probe caught and the original design missed.
    /// DROP TABLE permits the table owner, THE SCHEMA OWNER, or a superuser.
    /// With the audit tables in `public` — owned by the database owner —
    /// migration_role destroyed audit tables it did not own. The dedicated
    /// `audit` schema owned by audit_owner is what closes it.
    /// </summary>
    [Theory]
    [InlineData("DROP TABLE audit.audit_record")]
    [InlineData("DROP TABLE audit.audit_entity_ref")]
    [InlineData("DROP TABLE audit.audit_retention_policy")]
    [InlineData("DROP SCHEMA audit CASCADE")]
    public async Task The_migration_role_cannot_destroy_the_trail(string attack)
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        var failure = await Refused(
            database.ConnectionFor(AuditBoundaryDatabase.MigrationRole),
            attack);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);

        Assert.Equal(5, await CountAuditTablesAsync(database));
    }

    [Theory]
    [InlineData("ALTER TABLE audit.audit_record DISABLE TRIGGER tg_audit_record_guard")]
    [InlineData("DROP TRIGGER tg_audit_record_guard ON audit.audit_record")]
    public async Task The_migration_role_cannot_remove_the_protections(string attack)
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        var failure = await Refused(
            database.ConnectionFor(AuditBoundaryDatabase.MigrationRole),
            attack);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    /// <summary>
    /// An owner can grant itself anything. migration_role is not an owner
    /// here, which is what makes withholding DELETE meaningful rather than
    /// decorative.
    /// </summary>
    [Fact]
    public async Task The_migration_role_cannot_grant_itself_delete()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        var failure = await Refused(
            database.ConnectionFor(AuditBoundaryDatabase.MigrationRole),
            $"GRANT DELETE ON audit.audit_record TO {AuditBoundaryDatabase.MigrationRole}");

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    [Theory]
    [InlineData("UPDATE audit.audit_record SET reason = 'rewritten'")]
    [InlineData("DELETE FROM audit.audit_record")]
    public async Task The_migration_role_cannot_touch_the_records(string attack)
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        await SeedCatalogueAsync(database);
        await ExecuteAsync(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            InsertRecord());

        var failure = await Refused(
            database.ConnectionFor(AuditBoundaryDatabase.MigrationRole),
            attack);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);

        Assert.Equal(1, await CountRecordsAsync(database));
    }

    // ------------------------------------------------------------------
    // The superuser bypass that ENABLE ALWAYS removes
    // ------------------------------------------------------------------

    /// <summary>
    /// Without ENABLE ALWAYS this passes for the attacker: replica mode
    /// suppresses ordinary triggers, and a superuser may set it. With it, the
    /// guard fires anyway and the row survives.
    /// </summary>
    [Theory]
    [InlineData("UPDATE audit.audit_record SET reason = 'rewritten'")]
    [InlineData("DELETE FROM audit.audit_record")]
    public async Task Replica_mode_does_not_bypass_the_guard(string attack)
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        await SeedCatalogueAsync(database);
        await ExecuteAsync(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            InsertRecord());

        var failure = await Refused(
            database.PrivilegedConnection,
            $"SET session_replication_role = replica; {attack}");

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);

        Assert.Equal(1, await CountRecordsAsync(database));
    }

    // ------------------------------------------------------------------
    // Structural facts the boundary depends on
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_audit_schema_and_every_object_in_it_belong_to_audit_owner()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        var schemaOwner = await ScalarAsync<string>(
            database.PrivilegedConnection,
            "SELECT pg_get_userbyid(nspowner) FROM pg_namespace "
            + "WHERE nspname = 'audit'");

        Assert.Equal(AuditBoundaryDatabase.OwnerRole, schemaOwner);

        var foreignOwners = await ScalarAsync<long>(
            database.PrivilegedConnection,
            """
            SELECT count(*) FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'audit'
              AND c.relkind IN ('r', 'i')
              AND pg_get_userbyid(c.relowner) <> 'audit_owner'
            """);

        Assert.Equal(0, foreignOwners);
    }

    /// <summary>
    /// The owner's authority is unremovable, so the boundary rests on nobody
    /// being able to become it. NOLOGIN closes the front door; no members
    /// closes SET ROLE.
    /// </summary>
    [Fact]
    public async Task Nothing_can_become_the_audit_owner()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        var canLogIn = await ScalarAsync<bool>(
            database.PrivilegedConnection,
            "SELECT rolcanlogin FROM pg_roles WHERE rolname = 'audit_owner'");

        Assert.False(canLogIn);

        var isSuperuser = await ScalarAsync<bool>(
            database.PrivilegedConnection,
            "SELECT rolsuper FROM pg_roles WHERE rolname = 'audit_owner'");

        Assert.False(isSuperuser);

        var members = await ScalarAsync<long>(
            database.PrivilegedConnection,
            """
            SELECT count(*) FROM pg_auth_members m
            JOIN pg_roles r ON r.oid = m.roleid
            WHERE r.rolname = 'audit_owner'
            """);

        Assert.Equal(0, members);
    }

    /// <summary>
    /// audit_owner is excluded, and the exclusion is the honest shape of the
    /// claim rather than a convenience. An owner's privileges are implicit
    /// and cannot be revoked away — PostgreSQL reports DELETE for it whatever
    /// the ACL says. What makes that safe is that nothing can become
    /// audit_owner, which <see cref="Nothing_can_become_the_audit_owner"/>
    /// asserts separately. Superusers are excluded for the same structural
    /// reason: PostgreSQL does not constrain them, so a test claiming
    /// otherwise would be false.
    ///
    /// Every remaining role — the ones something can actually authenticate
    /// as — must hold no DELETE at all.
    /// </summary>
    [Fact]
    public async Task No_reachable_role_holds_delete_on_the_trail()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        var holders = await ScalarAsync<string>(
            database.PrivilegedConnection,
            """
            SELECT coalesce(string_agg(DISTINCT r.rolname || ' on ' || t.name, ', '), '')
            FROM pg_roles r
            CROSS JOIN (VALUES ('audit.audit_record'), ('audit.audit_entity_ref')) AS t(name)
            WHERE NOT r.rolsuper
              AND r.rolname NOT LIKE 'pg\_%'
              AND r.rolname <> 'audit_owner'
              AND has_table_privilege(r.rolname, t.name, 'DELETE')
            """);

        Assert.Equal(string.Empty, holders);
    }

    [Fact]
    public async Task Every_protection_trigger_survives_replica_mode()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        // 'A' is ENABLE ALWAYS. 'O' — the default — would not fire under
        // session_replication_role = replica.
        var notAlways = await ScalarAsync<string>(
            database.PrivilegedConnection,
            """
            SELECT coalesce(string_agg(t.tgname || '=' || t.tgenabled::text, ', '), '')
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'audit'
              AND NOT t.tgisinternal
              AND t.tgenabled <> 'A'
            """);

        Assert.Equal(string.Empty, notAlways);
    }

    // ------------------------------------------------------------------
    // The anonymiser: the one permitted mutation, and its edges
    // ------------------------------------------------------------------

    /// <summary>
    /// audit_anonymiser is NOLOGIN until the erasure worker exists, so it is
    /// reached through SET ROLE from the privileged connection. That still
    /// exercises what matters: current_user is the anonymiser, so the AR21
    /// conditions and the AR20 column grant are the things being tested.
    /// </summary>
    [Theory]
    [InlineData(
        "UPDATE audit.audit_record SET reason = 'x'",
        PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData(
        "DELETE FROM audit.audit_record",
        PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData(
        "UPDATE audit.audit_record SET actor_display_name = 'Anonymised'",
        PostgresErrorCodes.RaiseException)]
    public async Task The_anonymiser_is_confined_to_the_declared_columns(
        string attack,
        string expected)
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        await SeedCatalogueAsync(database);
        await ExecuteAsync(
            database.ConnectionFor(AuditBoundaryDatabase.AppRole),
            InsertRecord());

        var failure = await Refused(
            database.PrivilegedConnection,
            $"SET ROLE {AuditBoundaryDatabase.AnonymiserRole}; {attack}");

        Assert.Equal(expected, failure.SqlState);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string InsertRecord()
        => """
        INSERT INTO audit.audit_record
            (audit_id, occurred_at, captured_at, event_type, event_version,
             write_path, regulatory_classification, reason_required,
             entity_type, operation_id)
        VALUES
            (gen_random_uuid(), now(), now(), 'SignInFailed', 1,
             'Autonomous', 'SecurityEvent', false,
             'Identity', gen_random_uuid())
        """;

    /// <summary>
    /// The catalogue is release DATA seeded by AUD-C4, which does not exist
    /// yet. AUD-S01 supplies only the rows its own records need, so the
    /// boundary can be proved without pulling that story forward.
    /// </summary>
    private static async Task SeedCatalogueAsync(AuditBoundaryDatabase database)
        => await ExecuteAsync(database.PrivilegedConnection, """
            INSERT INTO audit.audit_event_type VALUES
                ('SignInFailed', 1, 'UserManagement', 'Sign-in failed', NULL,
                 'SecurityEvent', false, 'Autonomous', 'Payload', NULL, false,
                 '[]', '[]', NULL, true);

            INSERT INTO audit.audit_event_origin VALUES
                ('SignInFailed', 1, 'Anonymous', true);
            """);

    private static async Task<int> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountRecordsAsync(AuditBoundaryDatabase database)
        => await ScalarAsync<long>(
            database.PrivilegedConnection,
            "SELECT count(*) FROM audit.audit_record");

    private static async Task<long> CountAuditTablesAsync(AuditBoundaryDatabase database)
        => await ScalarAsync<long>(
            database.PrivilegedConnection,
            "SELECT count(*) FROM pg_tables WHERE schemaname = 'audit' "
            + "AND tablename <> 'audit_schema_version'");

    /// <summary>
    /// Asserts the statement was refused and hands back the error, so each
    /// test can be specific about WHY — a privilege refusal and a trigger
    /// refusal are different controls and a test that accepted either would
    /// not notice one of them disappearing.
    /// </summary>
    private static async Task<PostgresException> Refused(
        string connectionString,
        string sql)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(connectionString, sql));

        return failure;
    }
}
