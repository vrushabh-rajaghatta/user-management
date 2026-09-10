using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// USR-C1 end to end: a real service provider, the real adapters, an
/// established execution context, dispatch through the pipeline, and the
/// resulting rows inspected in PostgreSQL.
///
/// Nothing here is stubbed. The point is to prove the assembled slice rather
/// than any one part of it — that authorisation, the uniqueness pre-checks, the
/// policy resolver, token generation, provenance stamping and the transaction
/// boundary all compose into one business operation.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class CreateUserIntegrationTests
{

    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_administrator_creates_a_user_an_identity_and_a_token()
    {
        await RunAsync(async (dispatcher, admin) =>
        {
            var command = NewCommand();

            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    command, CancellationToken.None);

            try
            {
                var user = await ReadUserAsync(result.UserId);

                Assert.Equal("Human", user.ActorType);
                Assert.Equal("Active", user.Status);
                Assert.Equal(command.FirstName, user.FirstName);
                Assert.Equal(command.LastName, user.LastName);
                Assert.Equal(command.DisplayName, user.DisplayName);
                Assert.Equal(command.Email, user.Email);

                // Provenance: the administrator, not the created user, and not
                // the System actor.
                Assert.Equal(admin.Value, user.CreatedBy);
                Assert.Equal(admin.Value, user.UpdatedBy);
                Assert.NotEqual(default, user.CreatedAt);
                Assert.NotEqual(default, user.UpdatedAt);

                var identity = await ReadIdentityAsync(result.UserIdentityId);

                Assert.Equal("Local", identity.IdentityType);
                Assert.Equal("Application", identity.IdentityProvider);
                Assert.Equal("Active", identity.Status);
                Assert.Equal(command.InitialUsername, identity.Username);
                Assert.Equal(result.UserId.Value, identity.UserId);
                Assert.Equal(admin.Value, identity.CreatedBy);

                // UI5 — a Local identity's SubjectId is its own primary key.
                Assert.Equal(
                    result.UserIdentityId.Value.ToString(),
                    identity.SubjectId);

                var token = await ReadTokenAsync(result.UserIdentityId);

                Assert.Equal("Activation", token.TokenType);
                Assert.Null(token.UsedAt);
                Assert.Null(token.InvalidatedAt);

                // CreatedBy = the administrator: admin-initiated, which is the
                // forensic signal distinguishing it from a self-service reset.
                Assert.Equal(admin.Value, token.CreatedBy);

                // Lifetime from the effective policy, a CAP.
                Assert.Equal(
                    SecurityBaseline.Current.ActivationTokenLifetime,
                    token.ExpiresAt - token.CreatedAt);

                // Inv. 15 — no credential row. Absence IS the pending state;
                // there is no PendingActivation status to drift out of step.
                Assert.Equal(0, await CountCredentialsAsync(result.UserIdentityId));
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    /// <summary>
    /// UT7. The stored value must be the SHA-256 of a secret, and the secret
    /// itself must appear nowhere in the row — a database disclosure must not
    /// hand over an outstanding activation.
    /// </summary>
    [Fact]
    public async Task The_plaintext_activation_token_is_never_persisted()
    {
        await RunAsync(async (dispatcher, admin) =>
        {
            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    NewCommand(), CancellationToken.None);

            try
            {
                var token = await ReadTokenAsync(result.UserIdentityId);

                Assert.Equal(64, token.TokenHash.Length);

                Assert.All(
                    token.TokenHash,
                    c => Assert.True(
                        char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'),
                        $"'{c}' is not lowercase hexadecimal."));

                // The composed token is "{id}.{secret}". If any column held the
                // delivered form, it would contain the token id followed by a
                // separator — nothing in the row does.
                Assert.DoesNotContain(
                    $"{token.Id}.",
                    await DumpTokenRowAsync(result.UserIdentityId),
                    StringComparison.Ordinal);

                // The result deliberately carries no plaintext either: there is
                // no notification channel yet, so it has no consumer.
                Assert.Equal(
                    new[] { "UserId", "UserIdentityId" },
                    typeof(CreateUserResult)
                        .GetProperties()
                        .Select(x => x.Name)
                        .Where(x => x != "EqualityContract"));
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    [Fact]
    public async Task A_duplicate_email_is_refused_as_a_domain_error()
    {
        await RunAsync(async (dispatcher, admin) =>
        {
            var first = NewCommand();

            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    first, CancellationToken.None);

            try
            {
                // Different case, same address: AU3 indexes lower(email).
                var duplicate = NewCommand() with
                {
                    Email = first.Email.ToUpperInvariant(),
                };

                var failure = await Assert
                    .ThrowsAsync<BusinessRuleViolationException>(
                        () => dispatcher
                            .SendAsync<CreateUserCommand, CreateUserResult>(
                                duplicate, CancellationToken.None));

                Assert.Equal(
                    "A user with this email address already exists.",
                    failure.Message);
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    [Fact]
    public async Task A_duplicate_username_is_refused_as_a_domain_error()
    {
        await RunAsync(async (dispatcher, admin) =>
        {
            var first = NewCommand();

            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    first, CancellationToken.None);

            try
            {
                var duplicate = NewCommand() with
                {
                    InitialUsername = first.InitialUsername.ToUpperInvariant(),
                };

                var failure = await Assert
                    .ThrowsAsync<BusinessRuleViolationException>(
                        () => dispatcher
                            .SendAsync<CreateUserCommand, CreateUserResult>(
                                duplicate, CancellationToken.None));

                Assert.Equal(
                    "A user identity with this username already exists.",
                    failure.Message);
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    /// <summary>
    /// One command, one transaction. A failure after the user and identity are
    /// tracked must leave none of the three rows behind — a user without an
    /// identity, or an identity without a token, is not a state this command
    /// may produce.
    /// </summary>
    [Fact]
    public async Task A_failure_mid_transaction_leaves_no_partial_user()
    {
        await RunAsync(async (dispatcher, admin) =>
        {
            var first = NewCommand();

            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    first, CancellationToken.None);

            try
            {
                // Passes both pre-checks — the username is free and the email
                // differs in case only — so it fails at the INSERT, after all
                // three rows have been tracked.
                var doomed = NewCommand() with
                {
                    Email = first.Email.ToUpperInvariant(),
                };

                await Assert.ThrowsAsync<BusinessRuleViolationException>(
                    () => dispatcher
                        .SendAsync<CreateUserCommand, CreateUserResult>(
                            doomed, CancellationToken.None));

                Assert.Equal(
                    0, await CountIdentitiesByUsernameAsync(doomed.InitialUsername));

                Assert.Equal(1, await CountUsersByEmailAsync(first.Email));
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    /// <summary>
    /// user.create is human-only (PE4), and the pipeline must refuse a caller
    /// who does not hold it — otherwise every handler would have to remember to
    /// check, which is the omission behaviour 3 exists to make impossible.
    /// </summary>
    [Fact]
    public async Task A_caller_holding_no_role_is_refused()
    {
        await RunAsync(
            roleCode: null,
            async (dispatcher, caller) =>
            {
                await Assert.ThrowsAsync<BusinessRuleViolationException>(
                    () => dispatcher
                        .SendAsync<CreateUserCommand, CreateUserResult>(
                            NewCommand(), CancellationToken.None));
            });
    }

    /// <summary>
    /// The sharp case: access-reviewer holds user.read but NOT user.create, so
    /// this proves the pipeline checks the SPECIFIC declared permission rather
    /// than merely that the caller holds some role. A caller with no roles at
    /// all cannot distinguish those two behaviours.
    /// </summary>
    [Fact]
    public async Task A_caller_holding_a_different_user_permission_is_refused()
    {
        await RunAsync(
            roleCode: "access-reviewer",
            async (dispatcher, caller) =>
            {
                var failure = await Assert
                    .ThrowsAsync<BusinessRuleViolationException>(
                        () => dispatcher
                            .SendAsync<CreateUserCommand, CreateUserResult>(
                                NewCommand(), CancellationToken.None));

                Assert.Contains("permission", failure.Message);
            });
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // No Establish call: the context stays unauthenticated.
        var dispatcher = scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>();

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => dispatcher.SendAsync<CreateUserCommand, CreateUserResult>(
                NewCommand(), CancellationToken.None));
    }

    // ------------------------------------------------------------- harness

    private static async Task RunAsync(
        Func<ICommandDispatcher, UserId, Task> body)
        => await RunAsync("user-administrator", body);

    /// <param name="roleCode">
    /// The seeded role the caller holds, or null for a caller holding none.
    /// </param>
    private static async Task RunAsync(
        string? roleCode,
        Func<ICommandDispatcher, UserId, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        // Seeded once and left in place: USR-C1 is audited, so the caller and
        // the assignment that authorised it are referenced by rows nobody can
        // delete. See PermanentTestCaller.
        var administrator = (await PermanentTestCaller.EnsureAsync(
            ConnectionString, roleCode)).UserId;

        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(administrator, ActorType.Human, TestActorIdentity.Human());

        await body(
            scope.ServiceProvider.GetRequiredService<ICommandDispatcher>(),
            administrator);
    }

    private static ServiceProvider BuildProvider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private static CreateUserCommand NewCommand()
    {
        var discriminator = Guid.NewGuid().ToString("N");

        return new CreateUserCommand(
            "Created",
            "Person",
            $"Created Person {discriminator[..8]}",
            $"created-{discriminator}@example.test",
            $"created-{discriminator[..12]}");
    }

    // ------------------------------------------------------------- readers

    private sealed record UserRow(
        string ActorType, string Status, string? FirstName, string? LastName,
        string DisplayName, string? Email, Guid CreatedBy, Guid UpdatedBy,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private static async Task<UserRow> ReadUserAsync(UserId id)
    {
        var row = await SingleAsync(
            """
            SELECT actor_type, status, first_name, last_name, display_name,
                   email, created_by, updated_by, created_at, updated_at
            FROM app_user WHERE id = @id
            """, id.Value);

        return new UserRow(
            (string)row[0]!, (string)row[1]!, row[2] as string, row[3] as string,
            (string)row[4]!, row[5] as string, (Guid)row[6]!, (Guid)row[7]!,
            Offset(row[8])!.Value, Offset(row[9])!.Value);
    }

    private sealed record IdentityRow(
        Guid UserId, string IdentityType, string IdentityProvider,
        string SubjectId, string? Username, string Status, Guid CreatedBy);

    private static async Task<IdentityRow> ReadIdentityAsync(UserIdentityId id)
    {
        var row = await SingleAsync(
            """
            SELECT user_id, identity_type, identity_provider, subject_id,
                   username, status, created_by
            FROM user_identity WHERE id = @id
            """, id.Value);

        return new IdentityRow(
            (Guid)row[0]!, (string)row[1]!, (string)row[2]!, (string)row[3]!,
            row[4] as string, (string)row[5]!, (Guid)row[6]!);
    }

    private sealed record TokenRow(
        Guid Id, string TokenType, string TokenHash, DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt,
        DateTimeOffset? InvalidatedAt, Guid CreatedBy);

    private static async Task<TokenRow> ReadTokenAsync(UserIdentityId identityId)
    {
        var row = await SingleAsync(
            """
            SELECT id, token_type, token_hash, created_at, expires_at,
                   used_at, invalidated_at, created_by
            FROM user_token WHERE user_identity_id = @id
            """, identityId.Value);

        return new TokenRow(
            (Guid)row[0]!, (string)row[1]!, (string)row[2]!,
            Offset(row[3])!.Value, Offset(row[4])!.Value,
            Offset(row[5]), Offset(row[6]), (Guid)row[7]!);
    }

    /// <summary>
    /// Every column of the token row, concatenated, so the assertion covers
    /// columns a targeted check would miss.
    /// </summary>
    private static async Task<string> DumpTokenRowAsync(UserIdentityId identityId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT user_token::text FROM user_token WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Npgsql surfaces timestamptz as DateTime through the untyped GetValue,
    /// so the reader's own value is normalised here rather than cast blindly.
    /// </summary>
    private static DateTimeOffset? Offset(object? value)
        => value switch
        {
            null => null,
            DateTimeOffset offset => offset,
            DateTime utc => new DateTimeOffset(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException(
                $"'{value.GetType()}' is not a timestamp."),
        };

    private static async Task<object?[]> SingleAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), $"No row found for {id}.");

        var values = new object?[reader.FieldCount];

        for (var i = 0; i < reader.FieldCount; i++)
            values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);

        return values;
    }

    private static Task<int> CountCredentialsAsync(UserIdentityId identityId)
        => ScalarAsync(
            "SELECT count(*) FROM credential WHERE user_identity_id = @value",
            identityId.Value);

    private static Task<int> CountIdentitiesByUsernameAsync(string username)
        => ScalarAsync(
            "SELECT count(*) FROM user_identity WHERE lower(username) = lower(@value)",
            username);

    private static Task<int> CountUsersByEmailAsync(string email)
        => ScalarAsync(
            "SELECT count(*) FROM app_user WHERE lower(email) = lower(@value)",
            email);

    private static async Task<int> ScalarAsync(string sql, object value)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("value", value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task DeleteActorAsync(UserId userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
        {
            """
            DELETE FROM user_token WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
            "DELETE FROM user_role WHERE user_id = @id",
            "DELETE FROM user_identity WHERE user_id = @id",
            "DELETE FROM app_user WHERE id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", userId.Value);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> IsProvisionedAsync()
        => await ScalarAsync(
            "SELECT count(*) FROM role WHERE code = @value",
            "user-administrator") > 0;

    private static string ConnectionString => TestDatabase.ConnectionString;

}
