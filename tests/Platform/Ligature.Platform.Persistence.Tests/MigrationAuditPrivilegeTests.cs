using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// PRV-C2's privilege expansion, script 005 (docs/requirements.md, PRV-C2 —
/// "The privilege expansion is a C2 security decision").
///
/// migration_role held NOTHING on the trail. Catalogue synchronisation runs as
/// migration_role and must emit PermissionCatalogUpdated, which is impossible
/// without changing that — so the change is made deliberately, and narrowly:
///
///     migration_role is permitted to append audit evidence for
///     release-controlled migration operations. It has no authority to read,
///     modify, or delete audit records.
///
/// The second test is the one that matters. A grant of INSERT is only safe
/// while the other three stay denied: that is what keeps AR19 true — a
/// committed audit record is not amendable by anything that can log in — and it
/// is the half a later "just give it SELECT while debugging" would quietly
/// undo.
/// </summary>
public sealed class MigrationAuditPrivilegeTests
{
    private const string Role = AuditBoundaryDatabase.MigrationRole;

    [Theory]
    [InlineData("audit.audit_record")]
    [InlineData("audit.audit_entity_ref")]
    public async Task The_migration_role_may_append_audit_evidence(string table)
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        Assert.True(
            await HasPrivilegeAsync(database.PrivilegedConnection, table, "INSERT"),
            $"{Role} cannot INSERT into {table}, so a catalogue synchronisation could not record itself.");
    }

    [Theory]
    [InlineData("audit.audit_record", "SELECT")]
    [InlineData("audit.audit_record", "UPDATE")]
    [InlineData("audit.audit_record", "DELETE")]
    [InlineData("audit.audit_entity_ref", "SELECT")]
    [InlineData("audit.audit_entity_ref", "UPDATE")]
    [InlineData("audit.audit_entity_ref", "DELETE")]
    public async Task The_migration_role_still_cannot_read_amend_or_remove_the_trail(
        string table,
        string privilege)
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        Assert.False(
            await HasPrivilegeAsync(database.PrivilegedConnection, table, privilege),
            $"{Role} holds {privilege} on {table}. Only INSERT was granted, deliberately.");
    }

    /// <summary>
    /// The ledger, read so synchronisation can verify the audit schema is
    /// current BEFORE it commits anything — the precondition that stops a run
    /// committing an authorization change it then cannot record.
    /// </summary>
    [Fact]
    public async Task The_migration_role_may_read_the_deployment_ledger()
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        Assert.True(
            await HasPrivilegeAsync(database.PrivilegedConnection, "audit.audit_schema_version", "SELECT"),
            $"{Role} cannot read the deployment ledger, so it cannot check its own precondition.");
    }

    /// <summary>
    /// Both sides, deliberately. Reading the ledger is a precondition check;
    /// writing it would let this role record that a script it never applied is
    /// already there, which is how a skipped 005 becomes invisible.
    /// </summary>
    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public async Task The_migration_role_cannot_write_the_deployment_ledger(string privilege)
    {
        await using var database = await AuditBoundaryDatabase.CreateAsync();

        Assert.False(
            await HasPrivilegeAsync(database.PrivilegedConnection, "audit.audit_schema_version", privilege),
            $"{Role} holds {privilege} on the deployment ledger. Only SELECT was granted.");
    }

    private static async Task<bool> HasPrivilegeAsync(
        string connectionString,
        string table,
        string privilege)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT has_table_privilege(@role, @table, @privilege)", connection);

        command.Parameters.AddWithValue("role", Role);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("privilege", privilege);

        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
