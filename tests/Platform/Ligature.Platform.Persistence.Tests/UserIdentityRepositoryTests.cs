using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// ExistsWithUsernameAsync mirrors ux_user_identity_local_username. The rule
/// under test is UI7's ABSOLUTE uniqueness — deliberately asymmetric with the
/// email rule, and the asymmetry is the thing most likely to be "corrected"
/// by someone who has just read the AU3 predicate.
///
/// Target database comes from LIGATURE_CONNECTION. These tests FAIL rather
/// than skip when PostgreSQL is unreachable — see TestDatabase.
/// </summary>
public sealed class UserIdentityRepositoryTests
{

    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_tracks_without_saving()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserIdentityRepository(context);

        var (user, identity) = NewLocalIdentity();

        try
        {
            context.Add(user);
            await repository.AddAsync(identity, CancellationToken.None);

            Assert.Equal(0, await CountIdentitiesAsync(identity.Id));
            Assert.Equal(EntityState.Added, context.Entry(identity).State);

            await context.SaveChangesAsync(CancellationToken.None);

            Assert.Equal(1, await CountIdentitiesAsync(identity.Id));
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    [Fact]
    public async Task A_local_username_in_use_is_a_conflict()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserIdentityRepository(context);

        var (user, identity) = NewLocalIdentity();

        try
        {
            Assert.False(
                await repository.ExistsWithUsernameAsync(
                    identity.Username!, CancellationToken.None),
                "The username is unused before the identity is written.");

            context.Add(user);
            await repository.AddAsync(identity, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);

            Assert.True(
                await repository.ExistsWithUsernameAsync(
                    identity.Username!, CancellationToken.None));
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    [Fact]
    public async Task The_comparison_is_case_insensitive_like_the_index()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserIdentityRepository(context);

        var (user, identity) = NewLocalIdentity();

        try
        {
            context.Add(user);
            await repository.AddAsync(identity, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);

            var shouted = identity.Username!.ToUpperInvariant();

            Assert.NotEqual(identity.Username, shouted, StringComparer.Ordinal);

            Assert.True(
                await repository.ExistsWithUsernameAsync(
                    shouted, CancellationToken.None));
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    /// <summary>
    /// UI7 is absolute — it has no Status predicate. This is the opposite of
    /// the email rule, and it is why identity reactivation must be a status
    /// flip rather than a new row: a second row for the same subject cannot
    /// exist. If someone adds a status filter here to "match" AU3, this fails.
    /// </summary>
    [Fact]
    public async Task An_inactive_local_identity_still_holds_its_username()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserIdentityRepository(context);

        var (user, identity) = NewLocalIdentity();

        try
        {
            context.Add(user);
            await repository.AddAsync(identity, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);

            Assert.True(
                await repository.ExistsWithUsernameAsync(
                    identity.Username!, CancellationToken.None),
                "Precondition: the username is held while the identity is active.");

            identity.Deactivate(new DeactivationStamp(Now, User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);

            Assert.True(
                await repository.ExistsWithUsernameAsync(
                    identity.Username!, CancellationToken.None),
                "UI7 is absolute: a deactivated identity keeps its username "
                + "forever, or historical logs become ambiguous.");
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    /// <summary>
    /// The index is Local-only, so an external identity carrying the same
    /// username is not a conflict. Dropping the identity_type filter would make
    /// this report a false conflict.
    /// </summary>
    [Fact]
    public async Task An_external_identity_does_not_reserve_the_username()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserIdentityRepository(context);

        var discriminator = Guid.NewGuid().ToString("N");
        var username = $"Ext-{discriminator[..12]}";

        var user = NewHuman();

        var external = UserIdentity.CreateExternal(
            UserIdentityId.New(),
            user.Id,
            ActorType.Human,
            IdentityProvider.Create("MicrosoftEntraID"),
            subjectId: $"subject-{discriminator}",
            username: username,
            Now,
            User.SystemUserId);

        try
        {
            context.Add(user);
            await repository.AddAsync(external, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);

            Assert.Equal(1, await CountIdentitiesAsync(external.Id));

            Assert.False(
                await repository.ExistsWithUsernameAsync(
                    username, CancellationToken.None),
                "UI7 covers Local identities only.");
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    /// <summary>
    /// The race the pre-check cannot close, and the exact failure Item 11 maps.
    /// </summary>
    [Fact]
    public async Task The_index_rejects_a_duplicate_the_pre_check_allowed()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserIdentityRepository(context);

        var (user, identity) = NewLocalIdentity();

        var secondUser = NewHuman();

        var duplicate = UserIdentity.CreateLocal(
            UserIdentityId.New(),
            secondUser.Id,
            ActorType.Human,
            identity.Username!.ToUpperInvariant(),
            Now,
            User.SystemUserId);

        try
        {
            context.Add(user);
            await repository.AddAsync(identity, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);

            await using var racing = CreateContext();
            var racingRepository = new UserIdentityRepository(racing);

            racing.Add(secondUser);
            await racingRepository.AddAsync(duplicate, CancellationToken.None);

            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => racing.SaveChangesAsync(CancellationToken.None));

            var postgres = Assert.IsType<PostgresException>(failure.InnerException);

            Assert.Equal("23505", postgres.SqlState);
            Assert.Equal("ux_user_identity_local_username", postgres.ConstraintName);
        }
        finally
        {
            await CleanUpAsync(user.Id);
            await CleanUpAsync(secondUser.Id);
        }
    }

    // ------------------------------------------ SES-C1 sign-in lookup (UI7)

    /// <summary>
    /// Names the invariant the two-round-trip implementation of
    /// FindLocalByUsernameAsync exists for: the match runs through PostgreSQL's
    /// lower(), the same fold as ux_user_identity_local_username.
    ///
    /// EF would fold the parameter in .NET under CurrentCulture instead, and
    /// that fold is not the database's — "IZMIR" lowercases to 'ızmır' under
    /// tr-TR but 'izmir' under en_US.utf8. A sign-in would then be refused for
    /// a username the index considers taken. A mutation catches the removal;
    /// this states why it must not be removed.
    ///
    /// It also pins the second half of that implementation: a whole tracked
    /// UserIdentity comes back, because SES-C1 carries it into session
    /// creation. FromSql cannot produce one — the entity owns a
    /// DeactivationStamp, and EF expects the owned type's property names rather
    /// than the column names a SELECT * returns — which is why the id is
    /// resolved in SQL and the entity loaded by key.
    /// </summary>
    [Fact]
    public async Task A_sign_in_lookup_matches_case_insensitively_and_returns_the_entity()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserIdentityRepository(context);

        var (user, identity) = NewLocalIdentity();

        try
        {
            context.Add(user);
            await repository.AddAsync(identity, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            var shouted = identity.Username!.ToUpperInvariant();

            Assert.NotEqual(identity.Username, shouted, StringComparer.Ordinal);

            var found = await repository.FindLocalByUsernameAsync(
                shouted, CancellationToken.None);

            Assert.NotNull(found);
            Assert.Equal(identity.Id, found!.Id);

            // A whole entity, not a projection: the caller needs UserId to
            // check ownership and ActorType to enforce the humans-only rule.
            Assert.Equal(identity.UserId, found.UserId);
            Assert.Equal(ActorType.Human, found.ActorType);
            Assert.Equal(IdentityType.Local, found.IdentityType);
            Assert.Equal(UserStatus.Active, found.Status);
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    /// <summary>
    /// UI7 is Local-only, so an external identity carrying the same username is
    /// not a sign-in candidate. Local authentication must not resolve through
    /// an identity that authenticates elsewhere.
    /// </summary>
    [Fact]
    public async Task A_sign_in_lookup_ignores_external_identities()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var context = CreateContext();
        var repository = new UserIdentityRepository(context);

        var discriminator = Guid.NewGuid().ToString("N");
        var username = $"Ext-{discriminator[..12]}";
        var user = NewHuman();

        var external = UserIdentity.CreateExternal(
            UserIdentityId.New(), user.Id, ActorType.Human,
            IdentityProvider.Create("MicrosoftEntraID"),
            subjectId: $"subject-{discriminator}", username: username,
            Now, User.SystemUserId);

        try
        {
            context.Add(user);
            await repository.AddAsync(external, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            Assert.Null(
                await repository.FindLocalByUsernameAsync(
                    username, CancellationToken.None));
        }
        finally
        {
            await CleanUpAsync(user.Id);
        }
    }

    private static (User User, UserIdentity Identity) NewLocalIdentity()
    {
        var user = NewHuman();
        var discriminator = Guid.NewGuid().ToString("N");

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(),
            user.Id,
            ActorType.Human,
            $"Local-{discriminator[..12]}",
            Now,
            User.SystemUserId);

        return (user, identity);
    }

    private static User NewHuman()
    {
        var discriminator = Guid.NewGuid().ToString("N");

        return User.CreateHuman(
            UserId.New(),
            "Identity",
            "Tester",
            $"Identity Tester {discriminator[..8]}",
            $"identity-{discriminator}@example.test",
            Now,
            User.SystemUserId);
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new FixedClock(Now),
                    executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    private static async Task<int> CountIdentitiesAsync(UserIdentityId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM user_identity WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", id.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// Identities first: user_identity.user_id references app_user.
    /// </summary>
    private static async Task CleanUpAsync(UserId userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using (var identities = new NpgsqlCommand(
            "DELETE FROM user_identity WHERE user_id = @id", connection))
        {
            identities.Parameters.AddWithValue("id", userId.Value);
            await identities.ExecuteNonQueryAsync();
        }

        await using var user = new NpgsqlCommand(
            "DELETE FROM app_user WHERE id = @id", connection);

        user.Parameters.AddWithValue("id", userId.Value);
        await user.ExecuteNonQueryAsync();
    }

    private static string ConnectionString => TestDatabase.ConnectionString;


    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
