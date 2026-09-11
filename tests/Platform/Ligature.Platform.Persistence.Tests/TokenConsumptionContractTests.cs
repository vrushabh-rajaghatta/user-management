using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Repositories;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// UT6's consumption predicate, and what it now refuses.
///
/// The frozen workbook's UT6 names three conditions — unused, uninvalidated,
/// unexpired. Two more belong to the same decision and were missing: the token
/// must be the TYPE the caller expected, and its subject must be ACTIVE. That
/// is recorded as an interpretation of UT6 rather than a defect in its
/// implementation, because the previous code was a faithful reading of the
/// rule as written (docs/requirements.md).
///
/// EVERY REFUSAL HERE IS ASSERTED TWICE. That the call returns null is the
/// weak half; that the token is still UNCONSUMED afterwards is the property
/// the predicates exist to deliver, and the only reason they belong inside the
/// conditional UPDATE rather than in the caller. A caller-side check could only
/// run after the token had already been burned, which would permanently
/// disable an account over a condition — a deactivation later reversed — that
/// was temporary.
///
/// The refusals are deliberately indistinguishable to the caller. There is no
/// test here for "the reason was wrong type" because the repository cannot say
/// so: the statement that decides reports only whether it consumed, and a
/// second read to classify the refusal would be a second interpretation of
/// token state, free to drift from the predicate that actually governs.
/// </summary>
public sealed class TokenConsumptionContractTests : IAsyncLifetime
{
    private ThrowawayDatabase _database = null!;

    private static readonly Guid SystemUser =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private Guid _user;
    private Guid _identity;

    // ------------------------------------------------------------------
    // It still works
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_live_token_of_the_expected_type_for_an_active_subject_is_consumed()
    {
        var (id, hash) = await SeedTokenAsync(TokenType.Activation);

        var identity = await ConsumeAsync(id, hash, TokenType.Activation);

        Assert.Equal(_identity, identity?.Value);
        Assert.True(await IsConsumedAsync(id));
    }

    // ------------------------------------------------------------------
    // Type — the confusion CRD-C2 would otherwise make live
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_password_reset_token_cannot_be_consumed_as_an_activation()
    {
        // The plaintext is an id and a secret; it carries no type at all. So
        // before this predicate, a legitimately issued reset token presented to
        // the activation endpoint matched on both and was consumed — which,
        // because activation inserts a credential unconditionally, is how an
        // identity ended up with two password hashes.
        var (id, hash) = await SeedTokenAsync(TokenType.PasswordReset);

        Assert.Null(await ConsumeAsync(id, hash, TokenType.Activation));
        Assert.False(await IsConsumedAsync(id));
    }

    [Fact]
    public async Task An_activation_token_cannot_be_consumed_as_a_password_reset()
    {
        // The contract is symmetric, which is the point of putting it on the
        // repository rather than in the activation handler: CRD-C2 inherits it
        // and cannot forget it.
        var (id, hash) = await SeedTokenAsync(TokenType.Activation);

        Assert.Null(await ConsumeAsync(id, hash, TokenType.PasswordReset));
        Assert.False(await IsConsumedAsync(id));
    }

    // ------------------------------------------------------------------
    // Subject — the same predicate SignIn and NotificationGate already use
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_token_whose_identity_is_inactive_is_not_consumed()
    {
        var (id, hash) = await SeedTokenAsync(TokenType.Activation);

        await SetStatusAsync("user_identity", _identity, "Inactive");

        Assert.Null(await ConsumeAsync(id, hash, TokenType.Activation));
        Assert.False(await IsConsumedAsync(id));
    }

    [Fact]
    public async Task A_token_whose_user_is_inactive_is_not_consumed()
    {
        // Either alone must refuse. An active identity belonging to a
        // deactivated person is exactly the case a check on one of the two
        // would miss.
        var (id, hash) = await SeedTokenAsync(TokenType.Activation);

        await SetStatusAsync("app_user", _user, "Inactive");

        Assert.Null(await ConsumeAsync(id, hash, TokenType.Activation));
        Assert.False(await IsConsumedAsync(id));
    }

    [Fact]
    public async Task A_token_whose_identity_and_user_are_both_inactive_is_not_consumed()
    {
        var (id, hash) = await SeedTokenAsync(TokenType.Activation);

        await SetStatusAsync("user_identity", _identity, "Inactive");
        await SetStatusAsync("app_user", _user, "Inactive");

        Assert.Null(await ConsumeAsync(id, hash, TokenType.Activation));
        Assert.False(await IsConsumedAsync(id));
    }

    /// <summary>
    /// The reason the subject check lives inside the UPDATE and not in the
    /// caller, stated as behaviour.
    ///
    /// Somebody deactivated in error still holds the activation mail they were
    /// sent. Reactivating them must be enough; reissuing a token should not be
    /// necessary, and would not be possible for them to request.
    /// </summary>
    [Fact]
    public async Task A_token_refused_for_an_inactive_subject_works_again_once_reactivated()
    {
        var (id, hash) = await SeedTokenAsync(TokenType.Activation);

        await SetStatusAsync("app_user", _user, "Inactive");

        Assert.Null(await ConsumeAsync(id, hash, TokenType.Activation));

        await SetStatusAsync("app_user", _user, "Active");

        Assert.Equal(
            _identity, (await ConsumeAsync(id, hash, TokenType.Activation))?.Value);
    }

    // ------------------------------------------------------------------
    // The three UT6 names, unchanged
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_used_token_is_not_consumed_again()
    {
        var (id, hash) = await SeedTokenAsync(TokenType.Activation);

        Assert.NotNull(await ConsumeAsync(id, hash, TokenType.Activation));
        Assert.Null(await ConsumeAsync(id, hash, TokenType.Activation));
    }

    [Fact]
    public async Task An_invalidated_token_is_not_consumed()
    {
        var (id, hash) = await SeedTokenAsync(TokenType.Activation, invalidated: true);

        Assert.Null(await ConsumeAsync(id, hash, TokenType.Activation));

        // Still unconsumed rather than used: invalidated and used are
        // distinct endings, and a refused consumption must not collapse one
        // into the other.
        Assert.False(await IsConsumedAsync(id));
    }

    [Fact]
    public async Task An_expired_token_is_not_consumed()
    {
        var (id, hash) = await SeedTokenAsync(TokenType.Activation, expired: true);

        Assert.Null(await ConsumeAsync(id, hash, TokenType.Activation));
        Assert.False(await IsConsumedAsync(id));
    }

    [Fact]
    public async Task A_token_presented_with_the_wrong_secret_is_not_consumed()
    {
        var (id, _) = await SeedTokenAsync(TokenType.Activation);

        Assert.Null(await ConsumeAsync(id, "not-the-hash", TokenType.Activation));
        Assert.False(await IsConsumedAsync(id));
    }

    [Fact]
    public async Task An_unknown_token_id_is_refused()
        => Assert.Null(
            await ConsumeAsync(UserTokenId.New(), "any-hash", TokenType.Activation));

    // ------------------------------------------------------------------

    private async Task<UserIdentityId?> ConsumeAsync(
        UserTokenId tokenId, string hash, TokenType expected)
    {
        await using var context = _database.CreateContext();

        return await new UserTokenRepository(context).TryConsumeAsync(
            tokenId, hash, expected, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    /// <summary>
    /// Asked as a boolean rather than by reading the timestamp back: the
    /// property under test is whether the token was consumed, and Npgsql hands
    /// back DateTime for a timestamptz through the untyped scalar, which is a
    /// cast this has no reason to perform.
    /// </summary>
    private async Task<bool> IsConsumedAsync(UserTokenId tokenId)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT used_at IS NOT NULL FROM user_token WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", tokenId.Value);

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task SetStatusAsync(string table, Guid id, string status)
        => await ExecuteAsync(
            $"UPDATE {table} SET status = '{status}' WHERE id = '{id}'");

    private async Task<(UserTokenId Id, string Hash)> SeedTokenAsync(
        TokenType type, bool invalidated = false, bool expired = false)
    {
        var id = UserTokenId.New();
        var hash = $"hash-{Guid.NewGuid():N}";

        // ck_user_token_expires_after_created means an expired token must have
        // been issued earlier still — and after G4 both columns are immutable,
        // so it is issued that way rather than backdated.
        var created = expired ? "now() - interval '2 hours'" : "now()";
        var expires = expired ? "now() - interval '1 hour'" : "now() + interval '72 hours'";

        await ExecuteAsync(
            $"""
             INSERT INTO user_token
                 (id, user_identity_id, token_type, token_hash, expires_at,
                  used_at, invalidated_at, created_at, created_by)
             VALUES
                 ('{id.Value}', '{_identity}', '{type}', '{hash}', {expires},
                  NULL, {(invalidated ? "now()" : "NULL")}, {created}, '{SystemUser}')
             """);

        return (id, hash);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        _database = await ThrowawayDatabase.CreateAsync();

        _user = Guid.NewGuid();
        _identity = Guid.NewGuid();

        // Seeded directly rather than provisioned: these tests need a subject
        // whose status they can flip, and nothing else the provisioner builds.
        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{SystemUser}', 'System', NULL, NULL, 'System', NULL, 'Active',
                  now(), '{SystemUser}', now(), '{SystemUser}'),
                 ('{_user}', 'Human', 'Jo', 'Smith', 'Jo Smith',
                  'jo-{Guid.NewGuid():N}@example.test', 'Active',
                  now(), '{SystemUser}', now(), '{SystemUser}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider, subject_id,
                  username, status, created_at, created_by)
             VALUES
                 ('{_identity}', '{_user}', 'Human', 'Local', 'Application',
                  '{_identity}', 'jo-{Guid.NewGuid():N}', 'Active', now(), '{SystemUser}');
             """);
    }

    public async Task DisposeAsync()
        => await _database.DisposeAsync();
}
