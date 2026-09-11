using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The database half of the invariant: an address containing a control
/// character cannot be stored, whatever route it arrives by.
///
/// EmailAddress refuses the same characters, but a domain check only binds code
/// that goes through the domain. These tests attack the table directly with raw
/// SQL — which is how a fixture, a migration, a support script or a future
/// repository could bypass the type entirely.
///
/// TWO MECHANISMS, NOT ONE. The CHECK constraint refuses U+0001 upward with
/// 23514. U+0000 never reaches it: PostgreSQL rejects a NUL in a varchar at the
/// text encoding boundary with 22021, before any constraint is evaluated. Both
/// are asserted, and separately, so the distinction stays visible.
/// </summary>
public sealed class EmailControlCharacterConstraintTests : IAsyncLifetime
{
    private AuditBoundaryDatabase _database = null!;

    public async Task InitializeAsync()
        => _database = await AuditBoundaryDatabase.CreateAsync();

    public async Task DisposeAsync()
        => await _database.DisposeAsync();

    [Theory]
    [InlineData('\u0001')]   // SOH — the lowest character the CHECK can see
    [InlineData('\u000D')]   // CR  — the injection vector
    [InlineData('\u000A')]   // LF
    [InlineData('\u001F')]   // US  — the top of C0
    [InlineData('\u007F')]   // DEL
    [InlineData('\u0080')]   // the bottom of C1
    [InlineData('\u009F')]   // the top of C1
    public async Task A_control_character_cannot_be_inserted(char control)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InsertUserAsync($"jo{control}hn@example.com"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);

        Assert.Contains(
            "ck_app_user_email_no_control_characters",
            failure.ConstraintName ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_control_character_cannot_be_introduced_by_an_update()
    {
        var id = await InsertUserAsync("john@example.com");

        // The constraint governs the column, not the insert path — an UPDATE
        // that smuggled one in afterwards would defeat a write-only check.
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(
                "UPDATE app_user SET email = @value WHERE id = @id",
                ("value", "john@exa\rmple.com"),
                ("id", id)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }

    [Fact]
    public async Task A_null_byte_is_refused_by_the_encoding_layer_not_the_constraint()
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InsertUserAsync("jo\u0000hn@example.com"));

        // 22021 invalid_text_representation, NOT 23514. PostgreSQL cannot
        // represent a NUL in a varchar at all, so the value is rejected before
        // any constraint runs. The CHECK names \x00 in its range for
        // completeness; it never gets the chance to fire on one.
        Assert.Equal("22021", failure.SqlState);
    }

    [Fact]
    public async Task An_ordinary_address_still_inserts_and_materialises_through_EF()
    {
        var id = await InsertUserAsync("john.smith@example.com");

        // The read converter is EmailAddress.Create and stays strict. That is
        // only safe because the constraint above makes an unloadable value
        // unstorable — so this proves the pair works, not merely the write.
        await using var context = new LigatureDbContext(
            new DbContextOptionsBuilder<LigatureDbContext>()
                .UseNpgsql(_database.PrivilegedConnection)
                .Options);

        var user = await context.Set<User>()
            .FirstAsync(x => x.Id == new UserId(id));

        Assert.Equal("john.smith@example.com", user.Email!.Value);
    }

    [Fact]
    public async Task A_null_email_is_permitted()
    {
        // app_user.email is nullable — an agent or the system actor has none —
        // and the IS NULL disjunct in the constraint is what keeps that true.
        var id = await InsertUserAsync(null);

        Assert.NotEqual(Guid.Empty, id);
    }

    // ------------------------------------------------------------------

    private async Task<Guid> InsertUserAsync(string? email)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            """
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email,
                 status, created_at, created_by, updated_at, updated_by)
            VALUES
                (@id, 'Human', 'John', 'Smith', 'John Smith', @email,
                 'Active', now(), @id, now(), @id)
            """,
            ("id", id),
            ("email", (object?)email ?? DBNull.Value));

        return id;
    }

    private async Task ExecuteAsync(
        string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection =
            new NpgsqlConnection(_database.PrivilegedConnection);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync();
    }
}
