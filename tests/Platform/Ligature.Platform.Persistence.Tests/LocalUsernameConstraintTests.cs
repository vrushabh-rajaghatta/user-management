using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The database half of the local username rule (docs/requirements.md, "Local
/// usernames refuse surrounding whitespace", WS3 and WS4; UW-7 and UW-8).
///
/// THE BACKSTOP, NOT THE DEFINITION. UserIdentity.ValidateUsernameBoundary is
/// the rule. The CHECK exists so that a fixture, a support script or a future
/// repository that bypasses the domain still cannot store what the domain
/// refuses — and UW-7 proves the two agree character by character, by
/// evaluating the constraint's own installed expression, not a copy of it.
///
/// Raw SQL on purpose. Every statement runs in a transaction that is rolled
/// back, so the shared database keeps nothing.
/// </summary>
public sealed class LocalUsernameConstraintTests
{
    private const string Whitespace = "ck_user_identity_local_username_no_surrounding_whitespace";

    private const string Required = "ck_user_identity_local_username_required";

    private static readonly string System = User.SystemUserId.Value.ToString();

    // ---------------------------------------------------------------- UW-7

    /// <summary>
    /// Every UTF-16 code unit but NUL (which PostgreSQL cannot store in text at
    /// all) and the surrogates (which are not characters on their own), at the
    /// start and at the end of a local username: the installed constraint
    /// refuses exactly the values the domain rule refuses.
    /// </summary>
    [Theory]
    [InlineData("leading")]
    [InlineData("trailing")]
    public async Task The_constraint_refuses_exactly_what_the_domain_rule_refuses(string position)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();

        var definition = await DefinitionAsync(connection, Whitespace);

        Assert.StartsWith("CHECK (", definition, StringComparison.Ordinal);

        var expression = definition["CHECK ".Length..];

        var username = position == "leading" ? "chr(cp) || 'ada'" : "'ada' || chr(cp)";

        // The expression reads identity_type and username; the derived table
        // supplies exactly those names, so it evaluates as it would on a row.
        // A NULL result passes a CHECK, and NOT (NULL) is not true, so it is
        // counted as admitted here too.
        await using var command = new NpgsqlCommand(
            $"""
             SELECT cp FROM (
                 SELECT cp, 'Local'::varchar AS identity_type, ({username})::varchar AS username
                 FROM generate_series(1, 65535) AS cp
                 WHERE cp NOT BETWEEN 55296 AND 57343
             ) AS candidate
             WHERE NOT {expression}
             ORDER BY cp
             """, connection);

        var refusedByDatabase = new List<int>();

        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                refusedByDatabase.Add(reader.GetInt32(0));
        }

        var refusedByDomain = Enumerable.Range(1, 65535)
            .Where(cp => cp is < 0xD800 or > 0xDFFF)
            .Where(cp => RefusedByDomain(position == "leading" ? $"{(char)cp}ada" : $"ada{(char)cp}"))
            .ToList();

        Assert.Equal(25, refusedByDomain.Count);
        Assert.Equal(refusedByDomain, refusedByDatabase);
    }

    // ---------------------------------------------------------------- UW-8

    [Theory]
    [InlineData(" ada")]
    [InlineData("ada ")]
    [InlineData("\tada")]
    [InlineData("ada\n")]
    [InlineData("\u00A0ada")]
    [InlineData("ada\u3000")]
    public async Task A_spaced_local_username_cannot_be_inserted(string username)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InTransactionAsync(async (connection, transaction) =>
                await InsertIdentityAsync(connection, transaction, "Local", username)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal(Whitespace, failure.ConstraintName);
    }

    /// <summary>The constraint governs the column, not the insert path — IDN-C2's future write included.</summary>
    [Fact]
    public async Task A_spaced_local_username_cannot_be_introduced_by_an_update()
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InTransactionAsync(async (connection, transaction) =>
            {
                var id = await InsertIdentityAsync(connection, transaction, "Local", $"plain-{Guid.NewGuid():N}");

                await using var command = new NpgsqlCommand(
                    "UPDATE user_identity SET username = ' ' || username WHERE id = @id", connection, transaction);
                command.Parameters.AddWithValue("id", id);

                await command.ExecuteNonQueryAsync();
            }));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal(Whitespace, failure.ConstraintName);
    }

    /// <summary>UI6: a local identity has a username, and it is not empty.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task A_local_identity_without_a_username_cannot_be_inserted(string? username)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InTransactionAsync(async (connection, transaction) =>
                await InsertIdentityAsync(connection, transaction, "Local", username)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal(Required, failure.ConstraintName);
    }

    /// <summary>Both constraints are about LOCAL usernames; an external identity's is the provider's.</summary>
    [Theory]
    [InlineData(" external ")]
    [InlineData(null)]
    public async Task An_external_identity_is_not_constrained(string? username)
        => await InTransactionAsync(async (connection, transaction) =>
            await InsertIdentityAsync(connection, transaction, "External", username));

    [Theory]
    [InlineData("ada.lovelace")]
    [InlineData("a da")]
    [InlineData("\u200Bada")]
    public async Task A_valid_local_username_is_admitted(string username)
        => await InTransactionAsync(async (connection, transaction) =>
            await InsertIdentityAsync(connection, transaction, "Local", $"{username}-{Guid.NewGuid():N}"));

    // ------------------------------------------------------------ harness

    private static bool RefusedByDomain(string username)
    {
        try
        {
            UserIdentity.ValidateUsernameBoundary(username);

            return false;
        }
        catch (DomainException)
        {
            return true;
        }
    }

    private static async Task<string> DefinitionAsync(NpgsqlConnection connection, string name)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
            WHERE conname = @name AND conrelid = 'user_identity'::regclass
            """, connection);
        command.Parameters.AddWithValue("name", name);

        var definition = await command.ExecuteScalarAsync() as string;

        Assert.True(definition is not null, $"The constraint {name} is not installed.");

        return definition!;
    }

    private static async Task InTransactionAsync(Func<NpgsqlConnection, NpgsqlTransaction, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await body(connection, transaction);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<Guid> InsertIdentityAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string identityType, string? username)
    {
        var user = Guid.NewGuid();
        var id = Guid.NewGuid();

        await using (var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{user}', 'Human', 'Username', 'Check', 'Username Check', 'username-{user:N}@example.test',
                     'Active', now(), '{System}', now(), '{System}');
             """, connection, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        var local = identityType == "Local";

        await using (var command = new NpgsqlCommand(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by)
             VALUES ('{id}', '{user}', 'Human', '{identityType}', '{(local ? "Application" : "EntraId")}',
                     '{(local ? id.ToString() : $"subject-{id:N}")}', @username, 'Active', now(), '{System}');
             """, connection, transaction))
        {
            command.Parameters.AddWithValue("username", (object?)username ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        return id;
    }
}
