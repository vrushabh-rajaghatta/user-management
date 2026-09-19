using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.UpdateUserProfile;
using Ligature.Platform.Application.Users.Queries.UserProfile;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// USR-C2 and USR-Q1 v1 end to end, through the real pipeline and a real
/// database (docs/requirements.md, "USR-C2 — Update User Profile, and USR-Q1
/// GetUser (narrow v1)", P-A1..P-A8, G-A1..G-A4).
///
/// Its own provisioned database: the administrator becomes the actor of audit
/// records, which can never be deleted.
/// </summary>
public sealed class UserProfileIntegrationTests : IClassFixture<ActivationDatabase>
{
    private readonly ActivationDatabase _database;

    public UserProfileIntegrationTests(ActivationDatabase database) => _database = database;

    // ================================================================ USR-C2

    /// <summary>P-A1 — normalised values stored, one record with exactly the three fields, no reason.</summary>
    [Fact]
    public async Task An_update_stores_the_normalised_names_and_records_exactly_them()
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync();

        await UpdateAsync(admin, target, "  Ada ", " King  Lovelace ", " Ada Lovelace  ");

        Assert.Equal(("Ada", "King  Lovelace", "Ada Lovelace"), await NamesAsync(target));

        var record = await RowAsync(
            """
            SELECT before::text, after::text, reason, actor_user_id
              FROM audit.audit_record
             WHERE event_type = 'UserProfileChanged' AND entity_id = @id
            """, target.Value);

        // Exactly these members with exactly these values. Compared as JSON
        // objects: jsonb does not keep key order (it sorts keys by length,
        // then bytes), so the text order is not something the record states.
        Assert.Equal(
            [("DisplayName", "John Leaver"), ("FirstName", "John"), ("LastName", "Leaver")],
            Members((string)record[0]!));
        Assert.Equal(
            [("DisplayName", "Ada Lovelace"), ("FirstName", "Ada"), ("LastName", "King  Lovelace")],
            Members((string)record[1]!));
        Assert.Equal(DBNull.Value, record[2]);
        Assert.Equal(admin.Value, record[3]);
    }

    /// <summary>P-A4 — no change, even by surrounding whitespace: no write, no record.</summary>
    [Fact]
    public async Task No_change_writes_nothing_and_records_nothing()
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync();
        var updatedAt = await ScalarAsync<DateTime>("SELECT updated_at FROM app_user WHERE id = @id", target.Value);

        await UpdateAsync(admin, target, " John", "Leaver ", " John Leaver ");

        Assert.Equal(updatedAt, await ScalarAsync<DateTime>("SELECT updated_at FROM app_user WHERE id = @id", target.Value));
        Assert.Equal(0L, await AuditCountAsync(target));
    }

    /// <summary>P-A3 — a rule violation is refused through the pipeline, naming the field, and writes nothing.</summary>
    [Fact]
    public async Task A_name_breaking_a_rule_is_refused_and_writes_nothing()
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync();

        await AssertRefusedAsync(
            () => UpdateAsync(admin, target, "John", "Leaver", new string('x', 101)),
            "Display name must be at most 100 characters.");

        Assert.Equal(("John", "Leaver", "John Leaver"), await NamesAsync(target));
        Assert.Equal(0L, await AuditCountAsync(target));
    }

    /// <summary>P-A5 — an inactive user's profile may be corrected; their status stays.</summary>
    [Fact]
    public async Task An_inactive_users_profile_can_be_corrected()
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync(inactive: true);

        await UpdateAsync(admin, target, "Jon", "Leaver", "Jon Leaver");

        Assert.Equal(("Jon", "Leaver", "Jon Leaver"), await NamesAsync(target));
        Assert.Equal("Inactive", await ScalarAsync<string>("SELECT status FROM app_user WHERE id = @id", target.Value));
    }

    /// <summary>P-A6 — the System actor by the command itself; unknown; and the pipeline's permission check.</summary>
    [Fact]
    public async Task The_system_actor_an_unknown_user_and_a_caller_without_user_update_are_refused()
    {
        var admin = await CallerAsync("user-administrator");
        var reviewer = await CallerAsync("access-reviewer");
        var target = await SeedAsync();

        await AssertRefusedAsync(
            () => UpdateAsync(admin, User.SystemUserId, "Not", "Allowed", "Not Allowed"),
            "This user's profile cannot be changed.");

        await AssertRefusedAsync(
            () => UpdateAsync(admin, UserId.New(), "No", "One", "No One"),
            "The user does not exist.");

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => UpdateAsync(reviewer, target, "Jon", "Leaver", "Jon Leaver"));
        Assert.Contains("does not have permission", refusal.Message);

        Assert.Equal(("John", "Leaver", "John Leaver"), await NamesAsync(target));
        Assert.Equal("System", await ScalarAsync<string>("SELECT display_name FROM app_user WHERE id = @id", User.SystemUserId.Value));
    }

    /// <summary>P-A7 — nothing but the three names moves.</summary>
    [Fact]
    public async Task Nothing_but_the_three_names_changes()
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync();

        var before = await SnapshotAsync(target);

        await UpdateAsync(admin, target, "Jon", "Leavers", "J. Leavers");

        Assert.Equal(before, await SnapshotAsync(target));
    }

    /// <summary>P-A8 — last write wins in v1; both saves are in the trail.</summary>
    [Fact]
    public async Task The_last_write_wins_and_both_are_recorded()
    {
        var admin = await CallerAsync("user-administrator");
        var target = await SeedAsync();

        await UpdateAsync(admin, target, "First", "Edit", "First Edit");
        await UpdateAsync(admin, target, "Second", "Edit", "Second Edit");

        Assert.Equal(("Second", "Edit", "Second Edit"), await NamesAsync(target));
        Assert.Equal(2L, await AuditCountAsync(target));
    }

    // ================================================================ USR-Q1 v1

    /// <summary>G-A1, G-A2, G-A4 — exactly the stored values, to any user.read holder, and no record.</summary>
    [Fact]
    public async Task GetUser_returns_the_stored_profile_and_records_nothing()
    {
        var reviewer = await CallerAsync("access-reviewer");
        var target = await SeedAsync();
        var records = await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record WHERE @id::text IS NOT NULL", "x");

        var profile = await ReadAsync(reviewer, target);

        // v2 (USR-Q1 GetUser v2, DV-1): the three list fields added, exactly.
        // Seeded Active, with a local identity and no credential: pending.
        Assert.Equal(
            new UserProfileResult(
                target, "John", "Leaver", "John Leaver",
                await ScalarAsync<string>("SELECT email FROM app_user WHERE id = @id", target.Value),
                UserStatus.Active,
                ActivationPending: true),
            profile);
        Assert.Equal(records, await ScalarAsync<long>("SELECT count(*) FROM audit.audit_record WHERE @id::text IS NOT NULL", "x"));
    }

    /// <summary>
    /// DV-1 — v2's added fields mean exactly what the list row means by them
    /// (USR-Q2 amendments 1 and 2): all four status/activationPending
    /// combinations, and a null email, read by GetUser and by the list for the
    /// same users, must agree field for field.
    /// </summary>
    [Fact]
    public async Task GetUser_v2_agrees_with_the_list_row_for_every_lifecycle_combination()
    {
        var reviewer = await CallerAsync("access-reviewer");

        var users = new[]
        {
            await SeedAsync(inactive: false, withCredential: true),
            await SeedAsync(inactive: false, withCredential: false),
            await SeedAsync(inactive: true, withCredential: true),
            await SeedAsync(inactive: true, withCredential: false),
            await SeedAsync(noEmail: true),

            // Where the two clauses of activationPending part: no local
            // identity and no credential. The list says NOT pending (there is
            // nothing local to activate); a derivation that only asked "is
            // there no credential?" would say pending. One projection means
            // both views say the same.
            await SeedAsync(externalOnly: true),
        };

        var rows = await ListRowsAsync();

        foreach (var user in users)
        {
            var detail = await ReadAsync(reviewer, user);
            var row = Assert.Single(rows, x => x.UserId == user);

            Assert.Equal(
                (row.Email, row.Status, row.ActivationPending),
                (detail.Email, detail.Status, detail.ActivationPending));
        }

        // The combinations really are all present, so agreement is not vacuous.
        var seen = users.Select(u => rows.Single(x => x.UserId == u)).ToList();
        Assert.Contains(seen, x => x.Status == UserStatus.Active && !x.ActivationPending);
        Assert.Contains(seen, x => x.Status == UserStatus.Active && x.ActivationPending);
        Assert.Contains(seen, x => x.Status == UserStatus.Inactive && !x.ActivationPending);
        Assert.Contains(seen, x => x.Status == UserStatus.Inactive && x.ActivationPending);
        Assert.Contains(seen, x => x.Email is null);
        Assert.False(rows.Single(x => x.UserId == users[^1]).ActivationPending);
    }

    private async Task<IReadOnlyList<Ligature.Platform.Application.Users.Queries.UserList.UserListRow>> ListRowsAsync()
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IUserListReader>()
            .ReadAsync(0, 10_000, CancellationToken.None);
    }

    /// <summary>G-A3 — the System actor is unknown to this read, as is a missing user; G-A2, the permission.</summary>
    [Fact]
    public async Task GetUser_treats_the_system_actor_as_unknown_and_needs_user_read()
    {
        var admin = await CallerAsync("user-administrator");
        var unprivileged = await CallerAsync(null);

        await AssertRefusedAsync(() => ReadAsync(admin, User.SystemUserId), "The user does not exist.");
        await AssertRefusedAsync(() => ReadAsync(admin, UserId.New()), "The user does not exist.");

        var target = await SeedAsync();

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => ReadAsync(unprivileged, target));
        Assert.Contains("does not have permission", refusal.Message);
    }

    // ================================================================ harness

    /// <summary>A JSON object's members, name and string value, in ordinal name order.</summary>
    private static List<(string Name, string? Value)> Members(string json)
        => [.. System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject()
            .Select(x => (x.Name, x.Value.GetString()))
            .OrderBy(x => x.Name, StringComparer.Ordinal)];

    private async Task<UserId> CallerAsync(string? role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, $"usr-c2-{role ?? "unprivileged"}", role)).UserId;

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private async Task UpdateAsync(UserId caller, UserId target, string first, string last, string display)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        Establish(scope, caller);

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<UpdateUserProfileCommand, UpdateUserProfileResult>(
                new UpdateUserProfileCommand(target, first, last, display), CancellationToken.None);
    }

    private async Task<UserProfileResult> ReadAsync(UserId caller, UserId target)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        Establish(scope, caller);

        return await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<UserProfileQuery, UserProfileResult>(new UserProfileQuery(target), CancellationToken.None);
    }

    private static void Establish(IServiceScope scope, UserId caller)
        => scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

    private static async Task AssertRefusedAsync(Func<Task> dispatch, string message)
    {
        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(dispatch);
        Assert.Equal(message, refusal.Message);
    }

    private async Task<UserId> SeedAsync(
        bool inactive = false, bool withCredential = false, bool noEmail = false, bool externalOnly = false)
    {
        var id = Guid.NewGuid();
        var identity = Guid.NewGuid();
        var unique = id.ToString("N");
        var system = User.SystemUserId.Value;

        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by, deactivated_at, deactivated_by)
             VALUES ('{id}', 'Human', 'John', 'Leaver', 'John Leaver', {(noEmail ? "NULL" : $"'profile-{unique}@example.test'")},
                     '{(inactive ? "Inactive" : "Active")}', now() - interval '1 year', '{system}',
                     now() - interval '1 day', '{system}',
                     {(inactive ? "now() - interval '1 day'" : "NULL")}, {(inactive ? $"'{system}'" : "NULL")});

             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by,
                                        deactivated_at, deactivated_by)
             VALUES ('{identity}', '{id}', 'Human', {(externalOnly ? "'External', 'EntraId', 'external-" + unique + "', NULL" : $"'Local', 'Application', '{identity}', 'profile-{unique[..20]}'")},
                     '{(inactive ? "Inactive" : "Active")}', now() - interval '1 year', '{system}',
                     {(inactive ? "now() - interval '1 day'" : "NULL")}, {(inactive ? $"'{system}'" : "NULL")});
             """, connection);

        await command.ExecuteNonQueryAsync();

        if (withCredential)
        {
            var hashed = new Ligature.Platform.Persistence.Services.PasswordHasher().Hash("a-password-for-the-detail-test");

            await using var credential = new NpgsqlCommand(
                $"""
                 INSERT INTO credential (id, user_identity_id, identity_type, password_hash, password_algorithm,
                                         password_changed_at, must_change_password, failed_attempt_count,
                                         locked_until, created_at, created_by)
                 VALUES ('{Guid.NewGuid()}', '{identity}', 'Local', '{hashed.Hash}', '{hashed.Algorithm}',
                         now() - interval '1 day', false, 0, NULL, now() - interval '1 day', '{system}')
                 """, connection);

            await credential.ExecuteNonQueryAsync();
        }

        return new UserId(id);
    }

    private async Task<(string?, string?, string)> NamesAsync(UserId user)
    {
        var row = await RowAsync("SELECT first_name, last_name, display_name FROM app_user WHERE id = @id", user.Value);

        return (row[0] as string, row[1] as string, (string)row[2]!);
    }

    /// <summary>Everything about the user that is not a name, as one comparable string.</summary>
    private async Task<string> SnapshotAsync(UserId user)
        => await ScalarAsync<string>(
            """
            SELECT concat_ws('|', u.email, u.status, u.deactivated_at, u.created_at,
                   (SELECT string_agg(concat_ws(',', i.id, i.status, i.username), ';' ORDER BY i.id)
                      FROM user_identity i WHERE i.user_id = u.id),
                   (SELECT count(*) FROM user_role r WHERE r.user_id = u.id),
                   (SELECT count(*) FROM user_session s JOIN user_identity i ON i.id = s.user_identity_id WHERE i.user_id = u.id),
                   (SELECT count(*) FROM user_token t JOIN user_identity i ON i.id = t.user_identity_id WHERE i.user_id = u.id))
              FROM app_user u WHERE u.id = @id
            """, user.Value);

    private Task<long> AuditCountAsync(UserId user)
        => ScalarAsync<long>(
            "SELECT count(*) FROM audit.audit_record WHERE event_type = 'UserProfileChanged' AND entity_id = @id",
            user.Value);

    private async Task<T> ScalarAsync<T>(string sql, object id)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task<object?[]> RowAsync(string sql, object id)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "no row");

        var values = new object?[reader.FieldCount];
        reader.GetValues(values!);

        return values;
    }
}
