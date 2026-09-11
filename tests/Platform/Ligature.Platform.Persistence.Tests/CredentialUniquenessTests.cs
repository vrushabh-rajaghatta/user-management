using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// CR1 — one credential per identity, 1:0..1.
///
/// Deliberately its own class rather than part of the token-consumption tests
/// next door. The two land together but answer different questions: the
/// consumption contract closes the one path that could currently produce a
/// second credential, and this closes the SHAPE of the failure, whatever path
/// is invented later.
///
/// Why it is worth a database constraint rather than a code check. Activation
/// inserts a credential unconditionally — a fresh id, no lookup for an
/// existing row — and CredentialRepository resolves with FirstOrDefaultAsync.
/// So a second row would raise no error anywhere: it would be authentication
/// against whichever of two password hashes the query happened to return, with
/// no symptom to notice and nothing in the trail to explain it.
///
/// Attacked with raw SQL as the privileged connection, because the claim is
/// about the table and not about the repository that usually writes to it.
/// </summary>
public sealed class CredentialUniquenessTests : IAsyncLifetime
{
    private ThrowawayDatabase _database = null!;

    private static readonly Guid SystemUser =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private Guid _user;
    private Guid _identity;
    private Guid _otherIdentity;

    [Fact]
    public async Task An_identity_may_hold_one_credential()
        => await InsertCredentialAsync(_identity);

    [Fact]
    public async Task An_identity_cannot_hold_a_second_credential()
    {
        await InsertCredentialAsync(_identity);

        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InsertCredentialAsync(_identity));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);

        Assert.Contains(
            "IX_credential_user_identity_id_identity_type",
            failure.ConstraintName ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_identities_may_each_hold_their_own_credential()
    {
        // The constraint must not over-refuse. One person holding a local
        // identity per provider is ordinary, and each may have its own
        // password.
        await InsertCredentialAsync(_identity);
        await InsertCredentialAsync(_otherIdentity);
    }

    // ------------------------------------------------------------------

    private async Task InsertCredentialAsync(Guid identityId)
        => await ExecuteAsync(
            $"""
             INSERT INTO credential
                 (id, user_identity_id, identity_type, password_hash, password_algorithm,
                  password_changed_at, must_change_password, failed_attempt_count,
                  locked_until, created_at, created_by)
             VALUES
                 ('{Guid.NewGuid()}', '{identityId}', 'Local', 'a-hash', 'argon2id',
                  now(), false, 0, NULL, now(), '{SystemUser}')
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
                 (id, user_id, actor_type, identity_type, identity_provider, subject_id,
                  username, status, created_at, created_by)
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
