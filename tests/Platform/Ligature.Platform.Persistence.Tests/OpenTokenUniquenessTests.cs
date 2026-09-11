using Ligature.Platform.Domain.Users;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// UT4 — at most one OPEN token per (UserIdentityId, TokenType), where open
/// means unused and uninvalidated.
///
/// Its own class, attacking the table directly, because the claim is about the
/// index and not about the command that usually respects it. CRD-C2 calls UT5
/// before issuing; this proves what happens to anything that does not.
///
/// THE EXPIRY CASE IS THE POINT. The predicate deliberately says nothing about
/// expiry — a partial index must be immutable and now() is not — so an
/// expired-but-unused token still occupies the slot. That is what forces UT5
/// to invalidate expired tokens rather than skipping them as harmless, and it
/// is the test most likely to look wrong to someone who has not read the
/// index.
/// </summary>
public sealed class OpenTokenUniquenessTests : IAsyncLifetime
{
    private ThrowawayDatabase _database = null!;

    private static readonly Guid SystemUser =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private Guid _user;
    private Guid _identity;
    private Guid _otherIdentity;

    [Fact]
    public async Task One_open_token_per_type_is_permitted()
    {
        await InsertAsync(_identity, TokenType.Activation);
        await InsertAsync(_identity, TokenType.PasswordReset);
    }

    [Fact]
    public async Task A_second_open_token_of_the_same_type_is_refused()
    {
        await InsertAsync(_identity, TokenType.PasswordReset);

        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAsync(_identity, TokenType.PasswordReset));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);

        Assert.Contains(
            "ux_user_token_open_per_type",
            failure.ConstraintName ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_unused_token_still_occupies_the_slot()
    {
        // Expiry is derived from a timestamp, never stored as state, so the
        // index cannot see it. Issuing over an expired token therefore
        // requires invalidating it first — which is exactly what UT5 says.
        await InsertAsync(_identity, TokenType.PasswordReset, expired: true);

        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAsync(_identity, TokenType.PasswordReset));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);
    }

    [Fact]
    public async Task A_used_token_frees_the_slot()
    {
        await InsertAsync(_identity, TokenType.PasswordReset, used: true);

        await InsertAsync(_identity, TokenType.PasswordReset);
    }

    [Fact]
    public async Task An_invalidated_token_frees_the_slot()
    {
        // This is the transition UT5 performs, and the reason it works.
        await InsertAsync(_identity, TokenType.PasswordReset, invalidated: true);

        await InsertAsync(_identity, TokenType.PasswordReset);
    }

    [Fact]
    public async Task Two_identities_may_each_hold_an_open_token_of_one_type()
    {
        await InsertAsync(_identity, TokenType.PasswordReset);
        await InsertAsync(_otherIdentity, TokenType.PasswordReset);
    }

    // ------------------------------------------------------------------

    private async Task InsertAsync(
        Guid identityId, TokenType type,
        bool used = false, bool invalidated = false, bool expired = false)
        => await ExecuteAsync(
            $"""
             INSERT INTO user_token
                 (id, user_identity_id, token_type, token_hash, expires_at,
                  used_at, invalidated_at, created_at, created_by)
             VALUES
                 ('{Guid.NewGuid()}', '{identityId}', '{type}',
                  'hash-{Guid.NewGuid():N}',
                  {(expired ? "now() - interval '1 hour'" : "now() + interval '1 hour'")},
                  {(used ? "now()" : "NULL")},
                  {(invalidated ? "now()" : "NULL")},
                  {(expired ? "now() - interval '2 hours'" : "now()")},
                  '{SystemUser}')
             """);

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
        _otherIdentity = Guid.NewGuid();

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
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{_identity}', '{_user}', 'Human', 'Local', 'Application',
                  '{_identity}', 'jo-{Guid.NewGuid():N}', 'Active', now(), '{SystemUser}'),
                 ('{_otherIdentity}', '{_user}', 'Human', 'Local', 'Application',
                  '{_otherIdentity}', 'jo2-{Guid.NewGuid():N}', 'Active', now(), '{SystemUser}');
             """);
    }

    public async Task DisposeAsync()
        => await _database.DisposeAsync();
}
