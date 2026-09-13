using System.Security.Cryptography;
using System.Text;
using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.ResetPassword;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Services;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// CRD-C3 end to end: a real reset token, the real hasher, dispatch through the
/// pipeline, and the rows read back from PostgreSQL.
///
/// THE INVARIANT EVERY REFUSAL IS HELD TO: a token is burned only by a reset
/// that commits. A refusal is proven not by reading used_at once, but by
/// presenting the SAME token again with an acceptable password and watching
/// it succeed. That is the property users actually depend on.
///
/// WORDING IS NOT ASSERTED where it is not a frozen contract. The invalid-token
/// message is compared across cases for EQUALITY — its uniformity is the
/// contract, not its text — and password-policy refusals are asserted by type
/// and outcome only.
///
/// Its own provisioned database, for ActivationDatabase's reason: an account
/// that resets becomes the ACTOR of audit records and can never be removed.
/// </summary>
public sealed class ResetPasswordIntegrationTests
    : IClassFixture<ActivationDatabase>
{
    private const string Current = "the-current-password-1";
    private const string Fresh = "an-entirely-new-password-2";

    private readonly ActivationDatabase _database;

    public ResetPasswordIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ------------------------------------------------------------------
    // It works
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_valid_token_changes_the_password_and_appends_history()
    {
        var subject = await SeedAsync(lockedOut: true, mustChangePassword: true);
        var historyBefore = await CountHistoryAsync(subject.IdentityId);

        var result = await DispatchAsync(subject.Token, Fresh);

        Assert.Equal(subject.IdentityId, result.UserIdentityId.Value);

        var credential = await ReadCredentialAsync(subject.IdentityId);

        Assert.True(new PasswordHasher()
            .Verify(Fresh, credential.Hash, credential.Algorithm).IsValid);

        // A successful reset is the frozen way out of a lockout.
        Assert.Equal(0, credential.FailedAttemptCount);
        Assert.False(credential.IsLocked);

        // D2 — the user chose this password themselves.
        Assert.False(credential.MustChangePassword);

        // CR5 — history in the same transaction.
        Assert.Equal(historyBefore + 1, await CountHistoryAsync(subject.IdentityId));

        Assert.True(await IsConsumedAsync(subject.TokenId));
    }

    [Fact]
    public async Task A_consumed_token_cannot_reset_a_second_time()
    {
        var subject = await SeedAsync();

        await DispatchAsync(subject.Token, Fresh);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.Token, "yet-another-password-3"));

        Assert.Equal(await InvalidTokenMessageAsync(), failure.Message);
    }

    [Fact]
    public async Task The_reset_is_recorded_as_the_bearer_with_an_authenticated_origin()
    {
        var subject = await SeedAsync();

        await DispatchAsync(subject.Token, Fresh);

        foreach (var eventType in new[] { "TokenConsumed", "PasswordReset" })
        {
            var record = Assert.Single(await ReadRecordsAsync(subject.IdentityId, eventType));

            // The consumed token establishes the actor (AUD-D28): the person
            // who proved control of the mailbox, not the System actor.
            Assert.Equal("Authenticated", record.OriginKind);
            Assert.Equal(subject.UserId, record.ActorUserId);
            Assert.DoesNotContain(Fresh, record.Payload ?? "", StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------
    // Reuse — each row verified with ITS OWN stored parameters
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_current_password_cannot_be_chosen_again_and_the_token_survives()
    {
        var subject = await SeedAsync();

        // CR5 put the current password into history when it was set, so this
        // needs no separate "same as current" rule.
        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.Token, Current));

        Assert.False(await IsConsumedAsync(subject.TokenId));

        // The same token still works.
        await DispatchAsync(subject.Token, Fresh);
    }

    /// <summary>
    /// The testable half of the frozen trap: "compare against each history row
    /// using ITS stored algorithm, not the current one."
    ///
    /// A password set long ago at a lower work factor must still be recognised.
    /// An implementation that re-derived the new password with CURRENT
    /// parameters — or hashed it afresh and compared strings — would miss it.
    ///
    /// The other half, that the PasswordAlgorithm COLUMN is the one passed, is
    /// not provable while only one scheme exists: that value cannot change the
    /// verification outcome. Recorded in docs/requirements.md rather than
    /// claimed here.
    /// </summary>
    [Fact]
    public async Task A_password_stored_at_an_older_work_factor_is_still_recognised()
    {
        var subject = await SeedAsync();

        const string OldPassword = "a-password-from-years-ago-4";

        await InsertHistoryAsync(
            subject.IdentityId,
            LegacyHash(OldPassword, iterations: 1_000),
            createdMinutesAgo: 30);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.Token, OldPassword));

        Assert.False(await IsConsumedAsync(subject.TokenId));
    }

    [Fact]
    public async Task The_window_is_exactly_the_effective_history_depth()
    {
        var subject = await SeedAsync(currentInHistory: false);
        var depth = await EffectiveDepthAsync();

        // depth + 1 previous passwords, newest first. The newest `depth` are
        // inside the window; the one after them is not.
        for (var i = 0; i <= depth; i++)
        {
            await InsertHistoryAsync(
                subject.IdentityId,
                LegacyHash($"old-password-{i:D2}-padding", iterations: 1_000),
                createdMinutesAgo: 10 + i);
        }

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.Token, $"old-password-{depth - 1:D2}-padding"));

        Assert.False(await IsConsumedAsync(subject.TokenId));

        // Just outside the window: permitted.
        await DispatchAsync(subject.Token, $"old-password-{depth:D2}-padding");
    }

    /// <summary>
    /// D3 — a corrupt history row is corruption in our own data. It throws
    /// rather than being skipped, because skipping would quietly shorten the
    /// reuse window.
    /// </summary>
    [Fact]
    public async Task A_corrupt_history_row_fails_loudly_and_burns_nothing()
    {
        var subject = await SeedAsync();

        await InsertHistoryAsync(
            subject.IdentityId, "not-a-stored-hash-at-all", createdMinutesAgo: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DispatchAsync(subject.Token, Fresh));

        Assert.False(await IsConsumedAsync(subject.TokenId));
    }

    [Fact]
    public async Task A_password_below_the_floor_is_refused_and_the_token_survives()
    {
        var subject = await SeedAsync();

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.Token, "short"));

        Assert.False(await IsConsumedAsync(subject.TokenId));

        await DispatchAsync(subject.Token, Fresh);
    }

    // ------------------------------------------------------------------
    // Tokens that must not work
    // ------------------------------------------------------------------

    [Fact]
    public async Task Every_invalid_token_gets_the_same_message()
    {
        var subject = await SeedAsync();
        var expected = await InvalidTokenMessageAsync();

        var candidates = new[]
        {
            $"{Guid.NewGuid()}.{subject.Secret}",
            $"{subject.TokenId}.wrong-secret-value",
            "not-a-token",
            $"{subject.TokenId}.",
        };

        foreach (var token in candidates)
        {
            var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => DispatchAsync(token, Fresh));

            Assert.Equal(expected, failure.Message);
        }

        Assert.False(await IsConsumedAsync(subject.TokenId));
    }

    [Fact]
    public async Task An_activation_token_cannot_reset_a_password()
    {
        var subject = await SeedAsync(tokenType: TokenType.Activation);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.Token, Fresh));

        Assert.Equal(await InvalidTokenMessageAsync(), failure.Message);
        Assert.False(await IsConsumedAsync(subject.TokenId));
    }

    /// <summary>
    /// F2. CRD-C2 no longer issues a reset token to an identity with no
    /// credential, but one issued before that rule landed can still be live.
    /// It is refused exactly like an invalid token — and it must NEVER become a
    /// credential, which would make a reset a second activation path.
    /// </summary>
    [Fact]
    public async Task A_live_token_for_a_never_activated_identity_creates_nothing()
    {
        var subject = await SeedAsync(withCredential: false);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.Token, Fresh));

        Assert.Equal(await InvalidTokenMessageAsync(), failure.Message);
        Assert.Equal(0, await CountCredentialsAsync(subject.IdentityId));
        Assert.False(await IsConsumedAsync(subject.TokenId));
    }

    // ------------------------------------------------------------------

    private sealed record Subject(
        Guid UserId, Guid IdentityId, Guid TokenId, string Token, string Secret);

    private sealed record CredentialRow(
        string Hash, string Algorithm, int FailedAttemptCount,
        bool IsLocked, bool MustChangePassword);

    private sealed record RecordRow(string OriginKind, Guid? ActorUserId, string? Payload);

    private async Task<ResetPasswordResult> DispatchAsync(string token, string newPassword)
    {
        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        // No execution context: CRD-C3 is anonymous until the token proves it.
        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<ResetPasswordCommand, ResetPasswordResult>(
                new ResetPasswordCommand(token, newPassword),
                CancellationToken.None);
    }

    /// <summary>The uniform invalid-token message, captured rather than hard-coded.</summary>
    private async Task<string> InvalidTokenMessageAsync()
    {
        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync("not-a-token", Fresh));

        return failure.Message;
    }

    private async Task<Subject> SeedAsync(
        bool withCredential = true,
        bool currentInHistory = true,
        bool lockedOut = false,
        bool mustChangePassword = false,
        TokenType tokenType = TokenType.PasswordReset)
    {
        var unique = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Human', 'Reset', 'Bearer', 'Reset Bearer',
                  'bearer-{unique}@example.test', 'Active',
                  now() - interval '1 day', '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                  '{identityId}', 'bearer-{unique}', 'Active',
                  now() - interval '1 day', '{system}');
             """);

        if (withCredential)
        {
            var current = new PasswordHasher().Hash(Current);

            await ExecuteAsync(
                $"""
                 INSERT INTO credential
                     (id, user_identity_id, identity_type, password_hash,
                      password_algorithm, password_changed_at, must_change_password,
                      failed_attempt_count, locked_until, created_at, created_by)
                 VALUES
                     ('{Guid.NewGuid()}', '{identityId}', 'Local', '{current.Hash}',
                      '{current.Algorithm}', now() - interval '1 day',
                      {(mustChangePassword ? "true" : "false")},
                      {(lockedOut ? 5 : 0)},
                      {(lockedOut ? "now() + interval '1 hour'" : "NULL")},
                      now() - interval '1 day', '{system}')
                 """);

            if (currentInHistory)
                await InsertHistoryAsync(identityId, current.Hash, createdMinutesAgo: 60 * 24);
        }

        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        await ExecuteAsync(
            $"""
             INSERT INTO user_token
                 (id, user_identity_id, token_type, token_hash, expires_at,
                  used_at, invalidated_at, created_at, created_by)
             VALUES
                 ('{tokenId.Value}', '{identityId}', '{tokenType}', '{material.Hash}',
                  now() + interval '1 hour', NULL, NULL, now(), '{system}')
             """);

        var separator = material.PlainText.IndexOf('.');

        return new Subject(
            userId, identityId, tokenId.Value,
            material.PlainText, material.PlainText[(separator + 1)..]);
    }

    /// <summary>
    /// A hash in the stored format at a caller-chosen work factor — the shape
    /// a password set under an older policy would have. Built here rather than
    /// through the hasher, which only ever produces the current parameters.
    /// </summary>
    private static string LegacyHash(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations,
            HashAlgorithmName.SHA256, 32);

        return $"$pbkdf2-sha256$i={iterations}"
            + $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    private async Task InsertHistoryAsync(Guid identityId, string hash, int createdMinutesAgo)
        => await ExecuteAsync(
            $"""
             INSERT INTO password_history
                 (id, user_identity_id, password_hash, password_algorithm, created_at)
             VALUES
                 ('{Guid.NewGuid()}', '{identityId}', '{hash}', 'pbkdf2-sha256-v1',
                  now() - interval '{createdMinutesAgo} minutes')
             """);

    private async Task<int> EffectiveDepthAsync()
    {
        var stored = await ScalarAsync<int>(
            "SELECT password_history_depth FROM security_policy ORDER BY effective_from DESC LIMIT 1");

        // A FLOOR: max(tenant, baseline).
        return Math.Max(stored, SecurityBaseline.Current.PasswordHistoryDepth);
    }

    private async Task<CredentialRow> ReadCredentialAsync(Guid identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_algorithm, failed_attempt_count,
                   locked_until IS NOT NULL, must_change_password
            FROM credential WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        return new CredentialRow(
            reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
            reader.GetBoolean(3), reader.GetBoolean(4));
    }

    private async Task<IReadOnlyList<RecordRow>> ReadRecordsAsync(Guid identityId, string eventType)
    {
        var rows = new List<RecordRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT r.origin_kind, r.actor_user_id, r.payload::text
            FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = @type
              AND e.entity_type = 'Identity'
              AND e.entity_id = @id
            """, connection);

        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RecordRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows;
    }

    private Task<bool> IsConsumedAsync(Guid tokenId)
        => ScalarAsync<bool>($"SELECT used_at IS NOT NULL FROM user_token WHERE id = '{tokenId}'");

    private Task<long> CountHistoryAsync(Guid identityId)
        => ScalarAsync<long>($"SELECT count(*) FROM password_history WHERE user_identity_id = '{identityId}'");

    private Task<long> CountCredentialsAsync(Guid identityId)
        => ScalarAsync<long>($"SELECT count(*) FROM credential WHERE user_identity_id = '{identityId}'");

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
