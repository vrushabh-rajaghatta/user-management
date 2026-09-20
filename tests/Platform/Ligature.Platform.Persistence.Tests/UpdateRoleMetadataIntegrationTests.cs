using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Roles.Commands.CreateRole;
using Ligature.Platform.Application.Roles.Commands.UpdateRoleMetadata;
using Ligature.Platform.Application.Roles.Queries.RoleAdministration;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// AUT-C4 UpdateRoleMetadata end to end (docs/requirements.md, "AUT-C4
/// UpdateRoleMetadata", RM-A1 to RM-A11), through the real pipeline and a real
/// database.
///
/// THE NO-OP IS THE POINT (RM3). An edit that changes nothing must leave the
/// row and the audit trail untouched, which only a real database can show:
/// updated_at and updated_by are stamped by an interceptor, so "no write" is
/// provable as "the provenance did not move".
///
/// The roles these tests edit are created by AUT-C3 and cannot be removed —
/// AUT-C5 does not exist — so each test makes its own and leaves it.
/// </summary>
public sealed class UpdateRoleMetadataIntegrationTests : IClassFixture<ActivationDatabase>
{
    private const string SystemRole = "System roles cannot be modified.";

    private const string Unknown = "The role does not exist.";

    private readonly ActivationDatabase _database;

    public UpdateRoleMetadataIntegrationTests(ActivationDatabase database) => _database = database;

    // ---------------------------------------------------------------- RM-A1

    [Fact]
    public async Task A_tenant_role_is_renamed_and_answered_as_stored()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", "Reviews access.");

        var result = await UpdateAsync(admin, created.RoleId, "  Access Reviewer  ", " Reviews everything. ");

        Assert.Equal(created.RoleId, result.RoleId);
        Assert.Equal(created.Code, result.Code);
        Assert.Equal("Access Reviewer", result.Name);
        Assert.Equal("Reviews everything.", result.Description);
        Assert.False(result.IsSystemRole);
        Assert.True(result.IsActive);

        Assert.Equal(
            $"Access Reviewer|{created.Code}|Reviews everything.|f|t",
            await RowAsync(created.RoleId));
    }

    /// <summary>RM-A9: what the command has no input for does not move.</summary>
    [Fact]
    public async Task The_code_and_the_creation_provenance_are_untouched()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", null);
        var before = await CreationAsync(created.RoleId);

        await UpdateAsync(admin, created.RoleId, "Access Reviewer", "Reviews access.");

        Assert.Equal(before, await CreationAsync(created.RoleId));
    }

    // ---------------------------------------------------------------- RM-A2

    [Fact]
    public async Task The_change_is_recorded_once_with_before_and_after()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", "Reviews access.");

        await UpdateAsync(admin, created.RoleId, "Access Reviewer", "Reviews everything.");

        var record = await RecordAsync(created.RoleId, "RoleUpdated");

        Assert.Equal("RoleUpdated", record[0]);
        Assert.Equal(admin.Value, record[1]);
        Assert.Equal(DBNull.Value, record[2]);

        Assert.Equal(
            [("Description", "Reviews access."), ("Name", "Quality Reviewer")],
            Members((string)record[3]!));

        Assert.Equal(
            [("Description", "Reviews everything."), ("Name", "Access Reviewer")],
            Members((string)record[4]!));

        Assert.Equal(1, await RecordCountAsync(created.RoleId, "RoleUpdated"));
    }

    // ---------------------------------------------------------------- RM-A3

    [Theory]
    [InlineData("Quality Reviewer", "Reviews access.")]
    [InlineData("  Quality Reviewer  ", " Reviews access. ")]
    public async Task A_no_op_writes_nothing_and_records_nothing(string name, string? description)
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", "Reviews access.");
        var provenance = await ProvenanceAsync(created.RoleId);
        var sequence = await SequenceAsync();

        var result = await UpdateAsync(admin, created.RoleId, name, description);

        Assert.Equal("Quality Reviewer", result.Name);
        Assert.Equal("Reviews access.", result.Description);

        Assert.Equal(provenance, await ProvenanceAsync(created.RoleId));
        Assert.Equal(sequence, await SequenceAsync());
        Assert.Equal(0, await RecordCountAsync(created.RoleId, "RoleUpdated"));
    }

    /// <summary>A description that trims away, over one that is already absent.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_description_over_an_absent_one_is_a_no_op(string? description)
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", null);
        var provenance = await ProvenanceAsync(created.RoleId);

        Assert.Null((await UpdateAsync(admin, created.RoleId, "Quality Reviewer", description)).Description);

        Assert.Equal(provenance, await ProvenanceAsync(created.RoleId));
        Assert.Equal(0, await RecordCountAsync(created.RoleId, "RoleUpdated"));
    }

    // ---------------------------------------------------------------- RM-A4

    [Theory]
    [InlineData("", "A role name is required.")]
    [InlineData("   ", "A role name is required.")]
    [InlineData("Quality\u0007Reviewer", "A role name must not contain control characters.")]
    public async Task A_refused_name_changes_nothing(string name, string expected)
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", "Reviews access.");
        var provenance = await ProvenanceAsync(created.RoleId);

        Assert.Equal(expected, await RefusalAsync(() => UpdateAsync(admin, created.RoleId, name, null)));

        Assert.Equal("Quality Reviewer|Reviews access.", await NameAndDescriptionAsync(created.RoleId));
        Assert.Equal(provenance, await ProvenanceAsync(created.RoleId));
    }

    [Fact]
    public async Task A_description_is_cleared_to_none_and_stored_trimmed()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", "Reviews access.");

        Assert.Null((await UpdateAsync(admin, created.RoleId, "Quality Reviewer", "   ")).Description);
        Assert.Equal("Quality Reviewer|", await NameAndDescriptionAsync(created.RoleId));
    }

    // ---------------------------------------------------------------- RM-A5

    [Fact]
    public async Task A_system_role_is_refused_and_nothing_is_written()
    {
        var admin = await SecurityAdminAsync();
        var seeded = await SeededRoleAsync("access-reviewer");
        var provenance = await ProvenanceAsync(seeded);

        Assert.Equal(SystemRole, await RefusalAsync(() => UpdateAsync(admin, seeded, "Renamed", "Rewritten")));

        Assert.Equal(provenance, await ProvenanceAsync(seeded));
        Assert.Equal(0, await RecordCountAsync(seeded, "RoleUpdated"));
    }

    /// <summary>Identical values do not turn the refusal into a silent success.</summary>
    [Fact]
    public async Task A_system_role_is_refused_even_when_nothing_would_change()
    {
        var admin = await SecurityAdminAsync();
        var seeded = await SeededRoleAsync("user-administrator");
        var stored = await NameAndDescriptionAsync(seeded);
        var parts = stored.Split('|', 2);

        Assert.Equal(
            SystemRole,
            await RefusalAsync(() => UpdateAsync(admin, seeded, parts[0], parts[1].Length == 0 ? null : parts[1])));
    }

    // ---------------------------------------------------------------- RM-A6

    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        var admin = await SecurityAdminAsync();

        Assert.Equal(
            Unknown,
            await RefusalAsync(() => UpdateAsync(admin, RoleId.New(), "Quality Reviewer", null)));
    }

    // ---------------------------------------------------------------- RM-A7

    /// <summary>RM1: the code is the identifier; the name is metadata, and may collide.</summary>
    [Fact]
    public async Task A_role_may_be_renamed_to_a_name_another_role_already_holds()
    {
        var admin = await SecurityAdminAsync();
        var first = await CreateAsync(admin, "Shared Name", null);
        var second = await CreateAsync(admin, "Quality Reviewer", null);

        var result = await UpdateAsync(admin, second.RoleId, "Shared Name", null);

        Assert.Equal("Shared Name", result.Name);
        Assert.NotEqual(first.Code, result.Code);

        Assert.Equal("Shared Name|", await NameAndDescriptionAsync(first.RoleId));
        Assert.Equal("Shared Name|", await NameAndDescriptionAsync(second.RoleId));
    }

    /// <summary>Including a SEEDED role's name: uniqueness is nowhere, not merely among tenants.</summary>
    [Fact]
    public async Task A_role_may_be_renamed_to_a_seeded_roles_name()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", null);

        Assert.Equal("Access Reviewer", (await UpdateAsync(admin, created.RoleId, "Access Reviewer", null)).Name);
    }

    // ---------------------------------------------------------------- RM-A8

    [Fact]
    public async Task A_caller_without_role_manage_is_refused()
    {
        var reviewer = await CallerAsync("aut-c4-access-reviewer", "access-reviewer");
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", null);

        var refusal = await RefusalAsync(() => UpdateAsync(reviewer, created.RoleId, "Access Reviewer", null));

        Assert.Contains("permission", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Quality Reviewer|", await NameAndDescriptionAsync(created.RoleId));
    }

    // --------------------------------------------------------------- RM-A10

    /// <summary>Activity is AUT-C5/C6's concern: an inactive role's metadata may still be corrected.</summary>
    [Fact]
    public async Task An_inactive_tenant_role_can_still_be_edited()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", null);

        await ExecuteAsync($"UPDATE role SET is_active = false WHERE id = '{created.RoleId.Value}'");

        var result = await UpdateAsync(admin, created.RoleId, "Access Reviewer", null);

        Assert.Equal("Access Reviewer", result.Name);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task Nothing_but_the_role_and_its_record_is_written()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", null);

        var before = await CountsAsync();

        await UpdateAsync(admin, created.RoleId, "Access Reviewer", "Reviews access.");

        Assert.Equal(before, await CountsAsync());
    }

    // --------------------------------------------------------------- RM-A11

    /// <summary>
    /// The catalogue's claim that renaming is safe, made concrete: the read
    /// reports the new name, and the authority captured against an act already
    /// performed is untouched.
    ///
    /// WHAT THIS CAN AND CANNOT REACH TODAY. audit_record stores the
    /// authorising role's NAME in its own column beside the id, so a rename
    /// cannot reach it — a design that joined for the name would fail here.
    /// The sharper case, renaming the very role that authorised a recorded
    /// act, is UNREACHABLE until AUT-C7: only release-owned roles carry
    /// permissions today, a tenant role therefore never authorises anything,
    /// and a release-owned role cannot be renamed at all (RM4).
    /// </summary>
    [Fact]
    public async Task The_new_name_reaches_the_read_and_the_old_one_stays_in_the_record()
    {
        var admin = await SecurityAdminAsync();
        var created = await CreateAsync(admin, "Quality Reviewer", null);

        var captured = await AuthorisingRoleNamesAsync(created.RoleId);

        await UpdateAsync(admin, created.RoleId, "Access Reviewer", null);

        Assert.Contains(
            await ListAsync(admin),
            x => x.RoleId == created.RoleId.Value && x.Name == "Access Reviewer");

        Assert.Equal(captured, await AuthorisingRoleNamesAsync(created.RoleId));
    }

    // ================================================================ harness

    private async Task<CreateRoleResult> CreateAsync(UserId caller, string name, string? description)
        => await DispatchAsync<CreateRoleCommand, CreateRoleResult>(
            caller, new CreateRoleCommand($"tenant-{Guid.NewGuid():N}"[..24], name, description));

    private Task<UpdateRoleMetadataResult> UpdateAsync(UserId caller, RoleId roleId, string name, string? description)
        => DispatchAsync<UpdateRoleMetadataCommand, UpdateRoleMetadataResult>(
            caller, new UpdateRoleMetadataCommand(roleId, name, description));

    private static List<(string Name, string? Value)> Members(string json)
        => [.. System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject()
            .Select(x => (x.Name, x.Value.ValueKind == System.Text.Json.JsonValueKind.Null ? null : x.Value.GetString()))
            .OrderBy(x => x.Name, StringComparer.Ordinal)];

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
                new RoleAdministrationQuery(true, false), CancellationToken.None);

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

    private Task<UserId> SecurityAdminAsync() => CallerAsync("aut-c4-security-administrator", "security-administrator");

    private async Task<UserId> CallerAsync(string label, string role)
        => (await PermanentTestCaller.EnsureAsync(_database.ConnectionString, label, role)).UserId;

    private async Task<RoleId> SeededRoleAsync(string code)
        => new(await ScalarAsync<Guid>($"SELECT id FROM role WHERE code = '{code}'"));

    private Task<string> RowAsync(RoleId roleId)
        => ScalarAsync<string>(
            $"""
            SELECT concat_ws('|', name, code, description, is_system_role, is_active)
              FROM role WHERE id = '{roleId.Value}'
            """);

    /// <summary>Name and description alone, with an empty field for a null description.</summary>
    private Task<string> NameAndDescriptionAsync(RoleId roleId)
        => ScalarAsync<string>(
            $"SELECT concat_ws('|', name, coalesce(description, '')) FROM role WHERE id = '{roleId.Value}'");

    /// <summary>The interceptor stamps these on every write, so they move if and only if a write happened.</summary>
    private Task<string> ProvenanceAsync(RoleId roleId)
        => ScalarAsync<string>(
            $"SELECT concat_ws('|', updated_at, updated_by) FROM role WHERE id = '{roleId.Value}'");

    private Task<string> CreationAsync(RoleId roleId)
        => ScalarAsync<string>(
            $"SELECT concat_ws('|', code, is_system_role, created_at, created_by) FROM role WHERE id = '{roleId.Value}'");

    private Task<long> SequenceAsync()
        => ScalarAsync<long>("SELECT coalesce(max(sequence), 0) FROM audit.audit_record");

    private Task<long> RecordCountAsync(RoleId roleId, string eventType)
        => ScalarAsync<long>(
            $"""
            SELECT count(*) FROM audit.audit_record
             WHERE entity_id = '{roleId.Value}' AND event_type = '{eventType}'
            """);

    /// <summary>
    /// The authority captured by the act that CREATED this role — an act
    /// already performed when the rename happens. The name is a column of its
    /// own, copied at the time, not a join. (Every record about the role would
    /// be the wrong set: the rename writes one of its own.)
    /// </summary>
    private Task<string> AuthorisingRoleNamesAsync(RoleId roleId)
        => ScalarAsync<string>(
            $"""
            SELECT coalesce(string_agg(
                       concat_ws('|', authorizing_role_id, authorizing_role_name), ';' ORDER BY sequence), '')
              FROM audit.audit_record
             WHERE entity_id = '{roleId.Value}' AND event_type = 'RoleCreated'
            """);

    private Task<string> CountsAsync()
        => ScalarAsync<string>(
            """
            SELECT concat_ws('|',
                (SELECT count(*) FROM role),
                (SELECT count(*) FROM role_permission),
                (SELECT count(*) FROM user_role),
                (SELECT count(*) FROM permission))
            """);

    private async Task<object?[]> RecordAsync(RoleId roleId, string eventType)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT event_type, actor_user_id, reason, before::text, after::text
              FROM audit.audit_record
             WHERE entity_id = '{roleId.Value}' AND event_type = '{eventType}'
             ORDER BY sequence
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), $"no {eventType} record was written for the role");

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
