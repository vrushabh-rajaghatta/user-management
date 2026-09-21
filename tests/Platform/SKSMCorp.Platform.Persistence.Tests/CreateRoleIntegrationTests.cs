using SKSMCorp.Platform.Application;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Roles.Commands.CreateRole;
using SKSMCorp.Platform.Application.Roles.Queries.RoleAdministration;
using SKSMCorp.Platform.Application.Users.Commands.GrantRole;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// AUT-C3 CreateRole end to end (docs/requirements.md, "AUT-C3 CreateRole",
/// RC-A1 to RC-A9), through the real pipeline and a real database.
///
/// Its own provisioned database: a created role cannot be deleted or
/// deactivated — AUT-C5 does not exist — and it becomes the subject of an
/// audit record that can never be removed.
///
/// THE COLLISION RULE IS THE POINT. The database's unique index is
/// case-SENSITIVE, so the command refuses a case-only clash itself, and both
/// paths answer with one sentence.
/// </summary>
public sealed class CreateRoleIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string Collision = "A role with this code already exists.";

    private readonly ActivationDatabase _database;

    public CreateRoleIntegrationTests(ActivationDatabase database) => _database = database;

    // ---------------------------------------------------------------- RC-A1

    [Fact]
    public async Task A_role_is_created_as_an_active_tenant_role_and_answered_narrowly()
    {
        var admin = await SecurityAdminAsync();
        var code = UniqueCode();

        var result = await CreateAsync(admin, code, "  Quality Reviewer  ", " Reviews access. ");

        Assert.Equal(code, result.Code);
        Assert.Equal("Quality Reviewer", result.Name);
        Assert.Equal("Reviews access.", result.Description);
        Assert.False(result.IsSystemRole);
        Assert.True(result.IsActive);

        var row = await RowAsync(code);

        Assert.Equal(
            // PostgreSQL renders booleans as f and t.
            $"{result.RoleId.Value}|Quality Reviewer|{code}|Reviews access.|f|t|{admin.Value}",
            row);
    }

    // ---------------------------------------------------------------- RC-A2

    [Fact]
    public async Task The_creation_is_recorded_once_with_after_only()
    {
        var admin = await SecurityAdminAsync();
        var code = UniqueCode();

        var result = await CreateAsync(admin, code, "Quality Reviewer", "Reviews access.");

        var record = await RecordAsync(result.RoleId);

        Assert.Equal("RoleCreated", record[0]);
        Assert.Equal(admin.Value, record[1]);
        Assert.Equal(DBNull.Value, record[2]);
        Assert.Equal(DBNull.Value, record[3]);

        Assert.Equal(
            [("Code", code), ("Description", "Reviews access."), ("Name", "Quality Reviewer")],
            Members((string)record[4]!));

        Assert.Equal(1L, await ScalarAsync<long>(
            $"SELECT count(*) FROM audit.audit_record WHERE entity_id = '{result.RoleId.Value}'"));
    }

    // -------------------------------------------------------- RC-A3, RC-A4

    [Theory]
    [InlineData("", "A role code is required.")]
    [InlineData("   ", "A role code is required.")]
    [InlineData(" spaced-code", "A role code cannot begin or end with whitespace.")]
    [InlineData("spaced-code ", "A role code cannot begin or end with whitespace.")]
    public async Task An_invalid_code_is_refused_and_nothing_is_written(string code, string message)
    {
        var admin = await SecurityAdminAsync();
        var roles = await RoleCountAsync();

        Assert.Equal(message, await RefusalAsync(() => CreateAsync(admin, code, "Quality Reviewer", null)));
        Assert.Equal(roles, await RoleCountAsync());
    }

    [Fact]
    public async Task An_invalid_name_or_description_is_refused_and_nothing_is_written()
    {
        var admin = await SecurityAdminAsync();
        var roles = await RoleCountAsync();

        Assert.Equal(
            "A role name is required.",
            await RefusalAsync(() => CreateAsync(admin, UniqueCode(), "   ", null)));

        Assert.Equal(
            "A role name must be at most 100 characters.",
            await RefusalAsync(() => CreateAsync(admin, UniqueCode(), new string('x', 101), null)));

        Assert.Equal(
            "A role description must be at most 100 characters.",
            await RefusalAsync(() => CreateAsync(admin, UniqueCode(), "Quality Reviewer", new string('x', 101))));

        Assert.Equal(roles, await RoleCountAsync());
    }

    [Fact]
    public async Task A_blank_description_is_stored_as_none()
    {
        var admin = await SecurityAdminAsync();
        var code = UniqueCode();

        var result = await CreateAsync(admin, code, "Quality Reviewer", "   ");

        Assert.Null(result.Description);
        Assert.Equal(DBNull.Value, await ScalarAsync<object>($"SELECT description FROM role WHERE code = '{code}'"));
    }

    // ---------------------------------------------------------------- RC-A5

    [Fact]
    public async Task A_colliding_code_is_refused_exactly_or_by_case_alone()
    {
        var admin = await SecurityAdminAsync();
        var code = UniqueCode();

        await CreateAsync(admin, code, "Quality Reviewer", null);

        var roles = await RoleCountAsync();

        Assert.Equal(Collision, await RefusalAsync(() => CreateAsync(admin, code, "Another Name", null)));
        Assert.Equal(Collision, await RefusalAsync(() => CreateAsync(admin, code.ToUpperInvariant(), "Another Name", null)));

        // A release-owned code collides the same way, and says the same thing.
        Assert.Equal(Collision, await RefusalAsync(() => CreateAsync(admin, "access-reviewer", "Another Name", null)));
        Assert.Equal(Collision, await RefusalAsync(() => CreateAsync(admin, "ACCESS-REVIEWER", "Another Name", null)));

        Assert.Equal(roles, await RoleCountAsync());
    }

    // ---------------------------------------------------------------- RC-A6

    /// <summary>
    /// The index is the guarantee, the pre-check is the message. Two creates of
    /// one code, at once: one wins, the other reads the same sentence whether
    /// the pre-check or the index refused it.
    /// </summary>
    [Fact]
    public async Task Two_creates_of_one_code_leave_one_role_and_one_sentence()
    {
        var admin = await SecurityAdminAsync();
        var code = UniqueCode();

        var first = Task.Run(() => CreateAsync(admin, code, "Quality Reviewer", null));
        var second = Task.Run(() => CreateAsync(admin, code, "Quality Reviewer", null));

        var outcomes = await Task.WhenAll(Settled(first), Settled(second));

        Assert.Equal(1, outcomes.Count(x => x is null));
        Assert.Equal(Collision, Assert.Single(outcomes.Where(x => x is not null)));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM role WHERE code = '{code}'"));
    }

    // ---------------------------------------------------------------- RC-A7

    [Fact]
    public async Task A_caller_without_role_manage_is_refused()
    {
        var reviewer = await CallerAsync("aut-c3-access-reviewer", "access-reviewer");
        var roles = await RoleCountAsync();

        var refusal = await RefusalAsync(() => CreateAsync(reviewer, UniqueCode(), "Quality Reviewer", null));

        Assert.Contains("permission", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(roles, await RoleCountAsync());
    }

    // ---------------------------------------------------------------- RC-A8

    [Fact]
    public async Task The_created_role_is_readable_and_grantable()
    {
        var admin = await SecurityAdminAsync();
        var code = UniqueCode();

        var created = await CreateAsync(admin, code, "Quality Reviewer", null);

        var listed = (await ListAsync(admin)).Single(x => x.Code == code);

        Assert.Equal(0, listed.PermissionCount);
        Assert.Equal(0, listed.ActiveHolderCount);
        Assert.True(listed.AgentAssignable);
        Assert.True(listed.IsActive);
        Assert.False(listed.IsSystemRole);

        var holder = await SeedUserAsync();

        await DispatchAsync<GrantRoleCommand, GrantRoleResult>(
            admin, new GrantRoleCommand(holder, created.RoleId, null, null, "Pilot of the new role."));

        Assert.Equal(1, (await ListAsync(admin)).Single(x => x.Code == code).ActiveHolderCount);
    }

    // ---------------------------------------------------------------- RC-A9

    [Fact]
    public async Task Nothing_but_the_role_and_its_record_is_written()
    {
        var admin = await SecurityAdminAsync();
        var before = await ScalarAsync<string>(
            """
            SELECT concat_ws('|', (SELECT count(*) FROM permission), (SELECT count(*) FROM role_permission),
                   (SELECT count(*) FROM user_role), (SELECT count(*) FROM role))
            """);

        await CreateAsync(admin, UniqueCode(), "Quality Reviewer", null);

        var after = await ScalarAsync<string>(
            """
            SELECT concat_ws('|', (SELECT count(*) FROM permission), (SELECT count(*) FROM role_permission),
                   (SELECT count(*) FROM user_role), (SELECT count(*) FROM role))
            """);

        var (b, a) = (before.Split('|'), after.Split('|'));

        Assert.Equal(b[0], a[0]);
        Assert.Equal(b[1], a[1]);
        Assert.Equal(b[2], a[2]);
        Assert.Equal(int.Parse(b[3]) + 1, int.Parse(a[3]));
    }

    // ================================================================ harness

    private static string UniqueCode() => $"tenant-{Guid.NewGuid():N}"[..24];

    private static async Task<string?> Settled(Task<CreateRoleResult> task)
    {
        try
        {
            await task;

            return null;
        }
        catch (BusinessRuleViolationException refusal)
        {
            return refusal.Message;
        }
    }

    private static List<(string Name, string? Value)> Members(string json)
        => [.. System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject()
            .Select(x => (x.Name, x.Value.GetString()))
            .OrderBy(x => x.Name, StringComparer.Ordinal)];

    private Task<CreateRoleResult> CreateAsync(UserId caller, string code, string name, string? description)
        => DispatchAsync<CreateRoleCommand, CreateRoleResult>(caller, new CreateRoleCommand(code, name, description));

    private async Task<IReadOnlyList<RoleSummary>> ListAsync(UserId caller)
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        var result = await scope.ServiceProvider
            .GetRequiredService<IQueryDispatcher>()
            .SendAsync<RoleAdministrationQuery, RoleAdministrationResult>(
                new RoleAdministrationQuery(false, false), CancellationToken.None);

        return result.Roles;
    }

    private async Task<TResult> DispatchAsync<TCommand, TResult>(UserId caller, TCommand command)
        where TCommand : ICommand<TResult>
    {
        await using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(caller, ActorType.Human, TestActorIdentity.Human());

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<TCommand, TResult>(command, CancellationToken.None);
    }

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

    private static async Task<string> RefusalAsync(Func<Task> dispatch)
    {
        var refusal = await Assert.ThrowsAnyAsync<Exception>(dispatch);

        Assert.True(
            refusal is BusinessRuleViolationException or DomainException,
            $"Expected a refusal, got {refusal.GetType().Name}: {refusal.Message}");

        return refusal.Message;
    }

    private Task<UserId> SecurityAdminAsync() => CallerAsync("aut-c3-security-administrator", "security-administrator");

    private async Task<UserId> CallerAsync(string label, string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, role)).UserId;

    private async Task<UserId> SeedUserAsync()
    {
        var id = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by)
             VALUES ('{id}', 'Human', 'Role', 'Holder', 'Role Holder', 'holder-{id:N}@example.test',
                     'Active', now(), '{system}', now(), '{system}');
             """);

        return new UserId(id);
    }

    private Task<long> RoleCountAsync() => ScalarAsync<long>("SELECT count(*) FROM role");

    private Task<string> RowAsync(string code)
        => ScalarAsync<string>(
            $"""
            SELECT concat_ws('|', id, name, code, description, is_system_role, is_active, created_by)
              FROM role WHERE code = '{code}'
            """);

    private async Task<object?[]> RecordAsync(RoleId roleId)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT event_type, actor_user_id, reason, before::text, after::text
              FROM audit.audit_record WHERE entity_id = '{roleId.Value}'
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "no audit record was written for the role");

        var values = new object?[reader.FieldCount];
        reader.GetValues(values!);

        return values;
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
