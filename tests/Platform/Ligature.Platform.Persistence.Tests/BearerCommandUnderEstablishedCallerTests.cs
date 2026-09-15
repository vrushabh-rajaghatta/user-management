using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.ResetPassword;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The invariant for SES-C1, CRD-C1 and CRD-C3 together: a bearer-authenticated
/// identity-establishing command may execute only when no caller is already
/// established in the execution context.
///
/// Before it held, a scope that already carried a caller — which is what every
/// request with a live session is — could reach the credential and the token
/// before anything noticed that the bearer was somebody else. A correct
/// password for another account reset that account's lockout counters without
/// a record; a wrong one incremented the counter and then failed the request on
/// an audit emission defect, because the attempt's record would have carried an
/// Authenticated origin the catalogue does not permit. The difference between
/// those two outcomes told a signed-in caller whether a guess was right.
///
/// Every refusal is asserted on STATE first and on the exception second, so a
/// regression reports what it changed, not merely that the type differed.
///
/// Its own provisioned database (ActivationDatabase): the unchanged-behaviour
/// guards succeed, and a successful activation, reset or sign-in makes its user
/// the actor of permanent audit records.
/// </summary>
public sealed class BearerCommandUnderEstablishedCallerTests
    : IClassFixture<ActivationDatabase>
{
    private const string Password = "a-sufficiently-long-password";

    private const string Fresh = "an-entirely-new-password-2";

    private const string WrongPassword = "not-the-password-at-all";

    private readonly ActivationDatabase _database;

    public BearerCommandUnderEstablishedCallerTests(ActivationDatabase database)
        => _database = database;

    // ------------------------------------------------- refused: sign-in

    /// <summary>
    /// The oracle's "right" half. The password verifies — and nothing about
    /// B's credential may change: not the failure counter, not the stored
    /// algorithm a rehash would replace.
    /// </summary>
    [Fact]
    public async Task Established_A_signing_in_as_B_with_the_correct_password_is_refused_and_touches_nothing()
    {
        var a = await SeedAccountAsync();
        var b = await SeedAccountAsync(failedAttempts: 2, staleAlgorithm: true);

        var before = await ReadCredentialAsync(b.IdentityId);

        var outcome = await DispatchAsync<SignInCommand, SignInResult>(
            a, SignIn(b.Username, Password));

        Assert.Equal(before, await ReadCredentialAsync(b.IdentityId));
        Assert.Equal(0, await CountSessionsAsync(b.IdentityId));

        AssertRefused(outcome);
    }

    /// <summary>
    /// The oracle's "wrong" half, and the audit defect. No failed attempt may
    /// be counted against B, and the refusal is an authentication failure —
    /// not an emission defect that surfaces as a 500.
    /// </summary>
    [Fact]
    public async Task Established_A_signing_in_as_B_with_a_wrong_password_is_refused_and_counts_nothing()
    {
        var a = await SeedAccountAsync();
        var b = await SeedAccountAsync(failedAttempts: 2);

        var before = await ReadCredentialAsync(b.IdentityId);

        var outcome = await DispatchAsync<SignInCommand, SignInResult>(
            a, SignIn(b.Username, WrongPassword));

        Assert.Equal(before, await ReadCredentialAsync(b.IdentityId));
        Assert.Equal(0, await CountSessionsAsync(b.IdentityId));

        AssertRefused(outcome);
    }

    /// <summary>
    /// The same user is refused too. A command may not start under an
    /// established caller at all; the client ends the session first.
    /// </summary>
    [Fact]
    public async Task Established_A_signing_in_again_as_A_is_refused()
    {
        var a = await SeedAccountAsync();

        var before = await ReadCredentialAsync(a.IdentityId);

        var outcome = await DispatchAsync<SignInCommand, SignInResult>(
            a, SignIn(a.Username, Password));

        Assert.Equal(0, await CountSessionsAsync(a.IdentityId));
        Assert.Equal(before, await ReadCredentialAsync(a.IdentityId));

        AssertRefused(outcome);
    }

    // ------------------------------------------ refused: token bearers

    [Fact]
    public async Task Established_A_activating_B_with_a_valid_token_is_refused_and_the_token_survives()
    {
        var a = await SeedAccountAsync();
        var b = await SeedAccountAsync(withCredential: false, token: TokenType.Activation);

        var outcome = await DispatchAsync<ActivateAccountCommand, ActivateAccountResult>(
            a, new ActivateAccountCommand(b.Token!, Fresh));

        Assert.False(await IsConsumedAsync(b.TokenId!.Value));
        Assert.Equal(0L, await CountCredentialsAsync(b.IdentityId));

        AssertRefused(outcome);

        // Usable, not merely unconsumed: the same token activates once the
        // caller is gone.
        var retried = await DispatchAsync<ActivateAccountCommand, ActivateAccountResult>(
            caller: null, new ActivateAccountCommand(b.Token!, Fresh));

        Assert.Null(retried.Failure);
        Assert.True(await IsConsumedAsync(b.TokenId!.Value));
    }

    [Fact]
    public async Task Established_A_resetting_B_with_a_valid_token_is_refused_and_the_token_survives()
    {
        var a = await SeedAccountAsync();
        var b = await SeedAccountAsync(token: TokenType.PasswordReset);

        var before = await ReadCredentialAsync(b.IdentityId);

        var outcome = await DispatchAsync<ResetPasswordCommand, ResetPasswordResult>(
            a, new ResetPasswordCommand(b.Token!, Fresh));

        Assert.False(await IsConsumedAsync(b.TokenId!.Value));
        Assert.Equal(before, await ReadCredentialAsync(b.IdentityId));

        AssertRefused(outcome);

        var retried = await DispatchAsync<ResetPasswordCommand, ResetPasswordResult>(
            caller: null, new ResetPasswordCommand(b.Token!, Fresh));

        Assert.Null(retried.Failure);
        Assert.True(await IsConsumedAsync(b.TokenId!.Value));
    }

    // ------------------------------- unchanged: no caller established

    /// <summary>
    /// Guards, not proofs of the defect: these pass before and after. They
    /// exist so the refusal cannot be implemented by breaking the ordinary
    /// path.
    /// </summary>
    [Fact]
    public async Task Without_a_caller_sign_in_still_succeeds()
    {
        var b = await SeedAccountAsync();

        var outcome = await DispatchAsync<SignInCommand, SignInResult>(
            caller: null, SignIn(b.Username, Password));

        Assert.Null(outcome.Failure);
        Assert.True(outcome.Result!.Succeeded);
        Assert.Equal(1, await CountSessionsAsync(b.IdentityId));
    }

    [Fact]
    public async Task Without_a_caller_activation_still_succeeds()
    {
        var b = await SeedAccountAsync(withCredential: false, token: TokenType.Activation);

        var outcome = await DispatchAsync<ActivateAccountCommand, ActivateAccountResult>(
            caller: null, new ActivateAccountCommand(b.Token!, Fresh));

        Assert.Null(outcome.Failure);
        Assert.True(await IsConsumedAsync(b.TokenId!.Value));
        Assert.Equal(1L, await CountCredentialsAsync(b.IdentityId));
    }

    [Fact]
    public async Task Without_a_caller_password_reset_still_succeeds()
    {
        var b = await SeedAccountAsync(token: TokenType.PasswordReset);

        var outcome = await DispatchAsync<ResetPasswordCommand, ResetPasswordResult>(
            caller: null, new ResetPasswordCommand(b.Token!, Fresh));

        Assert.Null(outcome.Failure);
        Assert.True(await IsConsumedAsync(b.TokenId!.Value));
    }

    // -------------------------------------------- unchanged: replay

    /// <summary>
    /// The division of responsibility, proved. The unit of work runs the
    /// command's transaction twice in ONE scope — the first attempt is rolled
    /// back after the handler has already established the bearer — which is
    /// exactly what an execution strategy's retry does. The replay must still
    /// succeed: the refusal decides whether a command may START, and a retry
    /// of a command that started is not a new start.
    ///
    /// Attempts is asserted, so the test cannot pass because no replay happened.
    /// </summary>
    [Fact]
    public async Task A_replayed_sign_in_in_the_same_scope_still_succeeds()
    {
        var b = await SeedAccountAsync();

        await using var provider = BuildProvider(replayOnce: true);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<SignInCommand, SignInResult>(
                SignIn(b.Username, Password), CancellationToken.None);

        var unitOfWork = Assert.IsType<ReplayOnceUnitOfWork>(
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

        Assert.Equal(2, unitOfWork.Attempts);
        Assert.True(result.Succeeded);

        // Only the committed attempt's session exists.
        Assert.Equal(1, await CountSessionsAsync(b.IdentityId));
    }

    // ------------------------------------------------------------ harness

    private sealed record Account(
        UserId UserId,
        UserIdentityId IdentityId,
        string Username,
        string DisplayName,
        string Email,
        string? Token,
        Guid? TokenId);

    private sealed record Outcome<TResult>(TResult? Result, Exception? Failure);

    private sealed record CredentialRow(
        string Hash,
        string Algorithm,
        int FailedAttemptCount,
        DateTimeOffset? LockedUntil,
        DateTimeOffset PasswordChangedAt);

    private static SignInCommand SignIn(string username, string password)
        => new(username, password, "203.0.113.7", "bearer-guard-tests/1.0");

    /// <summary>
    /// The refusal the invariant requires: an authentication failure, raised
    /// before the command could touch anything.
    /// </summary>
    private static void AssertRefused<TResult>(Outcome<TResult> outcome)
    {
        Assert.True(
            outcome.Failure is AuthenticationFailedException,
            $"Expected AuthenticationFailedException; got "
            + $"{(outcome.Failure is null ? $"no exception (result: {outcome.Result})" : $"{outcome.Failure.GetType().Name}: {outcome.Failure.Message}")}.");
    }

    /// <summary>
    /// One command, one scope — as behind a host. When a caller is given, it is
    /// established on the scope first, exactly as caller middleware does for a
    /// request that carries a live session.
    /// </summary>
    private async Task<Outcome<TResult>> DispatchAsync<TCommand, TResult>(
        Account? caller, TCommand command)
        where TCommand : ICommand<TResult>
    {
        await using var provider = BuildProvider(replayOnce: false);
        using var scope = provider.CreateScope();

        if (caller is not null)
        {
            scope.ServiceProvider
                .GetRequiredService<IExecutionContextInitializer>()
                .Establish(
                    caller.UserId,
                    ActorType.Human,
                    new ActorIdentity(
                        caller.DisplayName,
                        caller.Username,
                        EmailAddress.Create(caller.Email),
                        IdentityProvider.Application,
                        SubjectId: caller.IdentityId.Value.ToString(),
                        CapturedAt: DateTimeOffset.UtcNow));
        }

        try
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<ICommandDispatcher>()
                .SendAsync<TCommand, TResult>(command, CancellationToken.None);

            return new Outcome<TResult>(result, null);
        }
        catch (Exception failure)
        {
            return new Outcome<TResult>(default, failure);
        }
    }

    private ServiceProvider BuildProvider(bool replayOnce)
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString);

        if (replayOnce)
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<UnitOfWork>();
            services.AddScoped<IUnitOfWork>(
                provider => new ReplayOnceUnitOfWork(
                    provider.GetRequiredService<UnitOfWork>()));
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// An active user and local identity; optionally a credential for
    /// <see cref="Password"/> and an open token. The identity's subject is its
    /// own id, so a caller can be established as exactly this identity.
    /// </summary>
    private async Task<Account> SeedAccountAsync(
        bool withCredential = true,
        int failedAttempts = 0,
        bool staleAlgorithm = false,
        TokenType? token = null)
    {
        var unique = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var username = $"bearer-guard-{unique[..12]}";
        var displayName = $"Bearer Guard {unique[..8]}";
        var email = $"bearer-guard-{unique}@example.test";
        var system = User.SystemUserId.Value;

        await using var connection = await _database.OpenAsync();

        await ExecuteAsync(
            connection,
            """
            INSERT INTO app_user
                (id, actor_type, first_name, last_name, display_name, email, status,
                 created_at, created_by, updated_at, updated_by)
            VALUES
                (@user, 'Human', 'Bearer', 'Guard', @displayName, @email, 'Active',
                 now() - interval '1 day', @system, now(), @system);

            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, created_at, created_by)
            VALUES
                (@identity, @user, 'Human', 'Local', 'Application',
                 @subject, @username, 'Active', now() - interval '1 day', @system);
            """,
            ("user", userId), ("identity", identityId), ("subject", identityId.ToString()),
            ("username", username), ("displayName", displayName), ("email", email),
            ("system", system));

        if (withCredential)
        {
            var hashed = new PasswordHasher().Hash(Password);

            await ExecuteAsync(
                connection,
                """
                INSERT INTO credential
                    (id, user_identity_id, identity_type, password_hash,
                     password_algorithm, password_changed_at, must_change_password,
                     failed_attempt_count, locked_until, created_at, created_by)
                VALUES
                    (@id, @identity, 'Local', @hash, @algorithm, now() - interval '1 day',
                     false, @failed, NULL, now() - interval '1 day', @system)
                """,
                ("id", Guid.NewGuid()), ("identity", identityId), ("hash", hashed.Hash),
                // A superseded algorithm name makes a successful verification
                // request a rehash — a second mutation a refusal must not make.
                ("algorithm", staleAlgorithm ? "pbkdf2-sha256-v0" : hashed.Algorithm),
                ("failed", failedAttempts), ("system", system));
        }

        string? plainText = null;
        Guid? tokenId = null;

        if (token is { } tokenType)
        {
            var id = UserTokenId.New();
            var material = new UserTokenService().Generate(id);

            await ExecuteAsync(
                connection,
                """
                INSERT INTO user_token
                    (id, user_identity_id, token_type, token_hash, expires_at,
                     used_at, invalidated_at, created_at, created_by)
                VALUES
                    (@id, @identity, @type, @hash, now() + interval '1 hour',
                     NULL, NULL, now(), @system)
                """,
                ("id", id.Value), ("identity", identityId), ("type", tokenType.ToString()),
                ("hash", material.Hash), ("system", system));

            plainText = material.PlainText;
            tokenId = id.Value;
        }

        return new Account(
            new UserId(userId), new UserIdentityId(identityId),
            username, displayName, email, plainText, tokenId);
    }

    private async Task<CredentialRow> ReadCredentialAsync(UserIdentityId identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_algorithm, failed_attempt_count,
                   locked_until, password_changed_at
            FROM credential WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "No credential row.");

        return new CredentialRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : Offset(reader.GetValue(3)),
            Offset(reader.GetValue(4))!.Value);
    }

    private async Task<bool> IsConsumedAsync(Guid tokenId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT used_at IS NOT NULL FROM user_token WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", tokenId);

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private Task<int> CountSessionsAsync(UserIdentityId identityId)
        => CountAsync("user_session", identityId);

    private async Task<long> CountCredentialsAsync(UserIdentityId identityId)
        => await CountAsync("credential", identityId);

    private async Task<int> CountAsync(string table, UserIdentityId identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE user_identity_id = @id", connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync();
    }

    private static DateTimeOffset? Offset(object? value)
        => value switch
        {
            null or DBNull => null,
            DateTimeOffset offset => offset,
            DateTime utc => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Not a timestamp."),
        };

    /// <summary>
    /// Runs the OUTERMOST transaction twice in the same scope: the first attempt
    /// runs the whole command and is then rolled back by a simulated transient
    /// failure, the second runs it again and commits — the shape of an
    /// execution-strategy retry, made deterministic. Calls nested inside the
    /// command (the handler enlisting in the pipeline's transaction) pass
    /// straight through.
    /// </summary>
    private sealed class ReplayOnceUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        private int _depth;

        public int Attempts { get; private set; }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            if (_depth > 0)
                return await inner.ExecuteInTransactionAsync(operation, cancellationToken);

            _depth++;

            try
            {
                try
                {
                    await inner.ExecuteInTransactionAsync<TResult>(
                        async ct =>
                        {
                            Attempts++;

                            await operation(ct);

                            throw new SimulatedTransientFailure();
                        },
                        cancellationToken);
                }
                catch (SimulatedTransientFailure)
                {
                    // Rolled back; replay below.
                }

                return await inner.ExecuteInTransactionAsync<TResult>(
                    async ct =>
                    {
                        Attempts++;

                        return await operation(ct);
                    },
                    cancellationToken);
            }
            finally
            {
                _depth--;
            }
        }
    }

    private sealed class SimulatedTransientFailure : Exception;
}
