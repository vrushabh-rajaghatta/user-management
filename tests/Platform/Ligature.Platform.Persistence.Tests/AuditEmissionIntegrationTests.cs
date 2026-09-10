using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The emission pipeline end to end: a real dispatch, the real behaviours,
/// the real catalogue loaded from the audit schema, and the resulting rows
/// read back out of PostgreSQL.
///
/// This is the evidence AUD-S01 was built for. Everything below could be
/// asserted against a fake writer and would prove nothing about the thing
/// that matters — that the application, which owns none of these tables and
/// holds only INSERT on them, can put a well-formed record into the trail
/// inside the same transaction as the business rows.
///
/// Records written here are permanent. There is no delete path for anyone,
/// which is the point; the trail of a shared development database grows by
/// three records each time this class runs.
/// </summary>
public sealed class AuditEmissionIntegrationTests
{
    /// <summary>
    /// USR-C1's three events, in the order AR16 requires, under one operation
    /// and one actor snapshot — the shape a reviewer reconstructs a business
    /// operation from.
    /// </summary>
    [Fact]
    public async Task USR_C1_lands_three_records_under_one_operation()
    {
        await RunAsync(async (dispatcher, administrator) =>
        {
            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    NewCommand(), CancellationToken.None);

            try
            {
                var records = await ReadOperationAsync(result.UserId);

                Assert.Equal(
                    ["UserCreated", "IdentityCreated", "TokenIssued"],
                    records.Select(x => x.EventType));

                // AR15 — one operation for the whole command.
                Assert.Single(records.Select(x => x.OperationId).Distinct());

                // Sequence follows declaration order, so the trail reads in
                // the order the handler did the work.
                Assert.Equal(
                    records.Select(x => x.Sequence).Order(),
                    records.Select(x => x.Sequence));

                // AR16 is not used here, and deliberately so: causation names
                // the record that CAUSED another, which within a single
                // command is already said by the shared operation. Declaring
                // it as well would assert a dependency between these three
                // events that the handler does not have.
                Assert.All(records, x => Assert.Null(x.CausationId));

                // Behaviour 16 — three timestamps with three different
                // meanings. OccurredAt is the command's, fixed once when the
                // transaction opened, so all three records share it; CapturedAt
                // is when the pipeline assembled the record, necessarily later.
                // CreatedAt is the database's own, and is asserted only to be
                // present: it comes from a different clock, so ordering it
                // against the other two would be asserting clock skew.
                Assert.Single(records.Select(x => x.OccurredAt).Distinct());
                Assert.All(records, x => Assert.True(x.CapturedAt >= x.OccurredAt));
                Assert.All(records, x => Assert.NotEqual(default, x.CreatedAt));

                // The subjects, from the command's own result.
                Assert.Equal(("User", result.UserId.Value), (records[0].EntityType, records[0].EntityId));
                Assert.Equal(("Identity", result.UserIdentityId.Value), (records[1].EntityType, records[1].EntityId));
                Assert.Equal("Token", records[2].EntityType);

                // The catalogue's values, captured onto the record (AUD-8).
                Assert.Equal("IdentityLifecycle", records[0].Classification);
                Assert.Equal("Transactional", records[0].WritePath);
                Assert.All(records, x => Assert.False(x.ReasonRequired));
                Assert.All(records, x => Assert.Null(x.Reason));
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    /// <summary>
    /// AR11/AR12/AR24 — the actor as they were at the moment they acted, not
    /// a foreign key to be resolved years later against rows that have since
    /// changed.
    /// </summary>
    [Fact]
    public async Task Every_record_carries_the_callers_snapshot_and_authority()
    {
        await RunAsync(async (dispatcher, administrator) =>
        {
            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    NewCommand(), CancellationToken.None);

            try
            {
                var records = await ReadOperationAsync(result.UserId);

                Assert.All(records, record =>
                {
                    Assert.Equal(administrator.Value, record.ActorUserId);
                    Assert.Equal("Human", record.ActorType);

                    // The values the execution context established, verbatim.
                    Assert.Equal("Test Person", record.ActorDisplayName);
                    Assert.Equal("test.person", record.ActorUsername);
                    Assert.Equal("test.person@example.test", record.ActorEmail);
                    Assert.Equal("Application", record.ActorIdentityProvider);
                    Assert.Equal("test-subject", record.ActorSubjectId);
                    Assert.Equal(TestActorIdentity.Captured, record.ActorCapturedAt);

                    // AR6 — the database derives the origin; nothing declares it.
                    Assert.Equal("Authenticated", record.OriginKind);

                    // AR12 — the assignment that authorised the command.
                    Assert.Equal("User Administrator", record.AuthorizingRoleName);
                    Assert.Equal("Global", record.AuthorizingScopeType);
                    Assert.NotNull(record.AuthorizingRoleId);
                    Assert.NotNull(record.AuthorizingAssignmentId);
                });
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    /// <summary>
    /// Four timestamps, four meanings, and the one that is easiest to lose is
    /// ActorCapturedAt. It is when the actor snapshot was READ, at
    /// establishment, and it is the reason a reviewer can tell whether the
    /// display name on a record was current at the time or has since changed.
    /// Stamping it at emission would destroy exactly that, and would still
    /// leave a plausible-looking timestamp in the column, so this asserts the
    /// value rather than its presence.
    ///
    ///     ActorCapturedAt   the actor snapshot was established
    ///     OccurredAt        the command opened its transaction
    ///     CapturedAt        the pipeline assembled the record
    ///     CreatedAt         the database wrote the row
    /// </summary>
    [Fact]
    public async Task The_actor_snapshot_is_stamped_when_it_was_established()
    {
        await RunAsync(async (dispatcher, administrator) =>
        {
            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    NewCommand(), CancellationToken.None);

            try
            {
                var records = await ReadOperationAsync(result.UserId);

                Assert.All(records, record =>
                {
                    Assert.Equal(TestActorIdentity.Captured, record.ActorCapturedAt);

                    // Establishment precedes the command, which precedes the
                    // capture. Three moments, in order, none of them reused.
                    Assert.True(
                        record.ActorCapturedAt < record.OccurredAt,
                        "ActorCapturedAt is not the moment the event occurred.");

                    Assert.True(
                        record.OccurredAt < record.CapturedAt,
                        "CapturedAt is not the moment the command opened.");
                });
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    /// <summary>
    /// AE1-AE4 — the entities a record touches beyond its primary subject.
    /// Without these, "who was this token issued to" is a join through rows
    /// that may since have been anonymised.
    /// </summary>
    [Fact]
    public async Task The_records_reference_the_entities_they_touch()
    {
        await RunAsync(async (dispatcher, administrator) =>
        {
            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    NewCommand(), CancellationToken.None);

            try
            {
                var records = await ReadOperationAsync(result.UserId);

                Assert.Empty(await ReadReferencesAsync(records[0].AuditId));

                Assert.Equal(
                    [("User", result.UserId.Value, "Subject")],
                    await ReadReferencesAsync(records[1].AuditId));

                Assert.Equal(
                    [
                        ("Identity", result.UserIdentityId.Value, "Target"),
                        ("User", result.UserId.Value, "Subject"),
                    ],
                    await ReadReferencesAsync(records[2].AuditId));
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    /// <summary>
    /// Behaviour 14's secret scan is a pipeline check; this is the outcome it
    /// exists to guarantee. The delivered token is "{id}.{secret}", so a
    /// record holding the delivered form would contain the token id followed
    /// by a separator — and the stored hash must not appear either.
    /// </summary>
    [Fact]
    public async Task No_token_material_reaches_the_trail()
    {
        await RunAsync(async (dispatcher, administrator) =>
        {
            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    NewCommand(), CancellationToken.None);

            try
            {
                var records = await ReadOperationAsync(result.UserId);
                var issued = records[2];
                var dump = await DumpRecordAsync(issued.AuditId);

                Assert.DoesNotContain($"{issued.EntityId}.", dump, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    await ReadTokenHashAsync(result.UserIdentityId), dump, StringComparison.Ordinal);

                // What the record does say: the kind of token and when it
                // expires — enough to review the decision, not enough to use.
                Assert.Contains("\"tokenType\": \"Activation\"", issued.Payload);
                Assert.Contains("\"expiresAt\"", issued.Payload);
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    /// <summary>
    /// AUD-4, and the other half of invariant 16. The writer refuses to run
    /// without a transaction rather than opening one of its own, because a
    /// transaction of its own would commit independently of the command: the
    /// business write could roll back with the record already permanent, and
    /// nothing downstream could tell that record from a true one.
    ///
    /// The guard is unreachable through the pipeline, since behaviour 6 always
    /// opens the transaction first. That is exactly why it needs a test: a
    /// regression here would be invisible until a pipeline was assembled
    /// without behaviour 6, which is the moment it matters most.
    /// </summary>
    [Fact]
    public async Task The_writer_refuses_to_run_outside_the_commands_transaction()
    {
        await TestDatabase.EnsureReachableAsync();

        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(ConnectionString)
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAuditRecordWriter>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.WriteAsync([], CancellationToken.None));

        Assert.StartsWith("Audit emission defect", failure.Message);
        Assert.Contains("AUD-4", failure.Message);
    }

    /// <summary>
    /// Invariant 16, which is the reason emission was put inside the
    /// command's transaction rather than after it: a business operation that
    /// did not happen leaves no record that it did.
    /// </summary>
    [Fact]
    public async Task A_rolled_back_command_leaves_no_records()
    {
        await RunAsync(async (dispatcher, administrator) =>
        {
            var first = NewCommand();

            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    first, CancellationToken.None);

            try
            {
                var before = await CountRecordsAsync();

                // Passes both pre-checks — the username is free and the email
                // differs in case only — so it fails at the INSERT, with the
                // audit rows for this attempt already declared.
                var doomed = NewCommand() with { Email = first.Email.ToUpperInvariant() };

                await Assert.ThrowsAsync<BusinessRuleViolationException>(
                    () => dispatcher.SendAsync<CreateUserCommand, CreateUserResult>(
                        doomed, CancellationToken.None));

                Assert.Equal(before, await CountRecordsAsync());
            }
            finally
            {
                await DeleteActorAsync(result.UserId);
            }
        });
    }

    /// <summary>
    /// A command the pipeline refuses never reaches the handler, so nothing
    /// is declared and nothing is written. The refusal itself is a Slice B
    /// event (AuthorizationDenied), not an omission here.
    /// </summary>
    [Fact]
    public async Task A_refused_command_writes_nothing()
    {
        await RunAsync("access-reviewer", async (dispatcher, administrator) =>
        {
            var before = await CountRecordsAsync();

            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => dispatcher.SendAsync<CreateUserCommand, CreateUserResult>(
                    NewCommand(), CancellationToken.None));

            Assert.Equal(before, await CountRecordsAsync());
        });
    }

    // ------------------------------------------------------------- harness

    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);

    private static async Task RunAsync(Func<ICommandDispatcher, UserId, Task> body)
        => await RunAsync("user-administrator", body);

    private static async Task RunAsync(
        string? roleCode, Func<ICommandDispatcher, UserId, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var administrator = (await PermanentTestCaller.EnsureAsync(
            ConnectionString, roleCode)).UserId;

        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(ConnectionString)
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(administrator, ActorType.Human, TestActorIdentity.Human());

        await body(
            scope.ServiceProvider.GetRequiredService<ICommandDispatcher>(),
            administrator);
    }

    private static CreateUserCommand NewCommand()
    {
        var discriminator = Guid.NewGuid().ToString("N");

        return new CreateUserCommand(
            "Audited",
            "Person",
            $"Audited Person {discriminator[..8]}",
            $"audited-{discriminator}@example.test",
            $"audited-{discriminator[..12]}");
    }

    // ------------------------------------------------------------- readers

    private sealed record Record(
        Guid AuditId, long Sequence, DateTimeOffset OccurredAt,
        DateTimeOffset CapturedAt, DateTimeOffset CreatedAt, string EventType,
        string WritePath, string Classification, bool ReasonRequired, string? Reason,
        Guid? ActorUserId, string? ActorType, string? ActorDisplayName,
        string? ActorUsername, string? ActorEmail, string? ActorIdentityProvider,
        string? ActorSubjectId, DateTimeOffset? ActorCapturedAt, string OriginKind,
        Guid? AuthorizingRoleId, string? AuthorizingRoleName,
        string? AuthorizingScopeType, Guid? AuthorizingAssignmentId,
        string EntityType, Guid? EntityId, Guid OperationId, Guid? CausationId,
        string? Payload);

    /// <summary>
    /// Every record of the operation that created this user, in sequence
    /// order. The operation is located from the one record whose subject the
    /// caller already knows.
    /// </summary>
    private static async Task<IReadOnlyList<Record>> ReadOperationAsync(UserId userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT audit_id, sequence, occurred_at, captured_at, created_at,
                   event_type, write_path, regulatory_classification,
                   reason_required, reason, actor_user_id, actor_type,
                   actor_display_name, actor_username, actor_email,
                   actor_identity_provider, actor_subject_id, actor_captured_at,
                   origin_kind, authorizing_role_id, authorizing_role_name,
                   authorizing_scope_type, authorizing_assignment_id,
                   entity_type, entity_id, operation_id, causation_id,
                   payload::text
            FROM audit.audit_record
            WHERE operation_id = (
                SELECT operation_id FROM audit.audit_record
                WHERE event_type = 'UserCreated' AND entity_id = @userId)
            ORDER BY sequence
            """, connection);

        command.Parameters.AddWithValue("userId", userId.Value);

        var records = new List<Record>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            records.Add(new Record(
                reader.GetGuid(0), reader.GetInt64(1), reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetBoolean(8), Nullable(reader, 9, x => x.GetString(9)),
                Nullable(reader, 10, x => (Guid?)x.GetGuid(10)),
                Nullable(reader, 11, x => x.GetString(11)),
                Nullable(reader, 12, x => x.GetString(12)),
                Nullable(reader, 13, x => x.GetString(13)),
                Nullable(reader, 14, x => x.GetString(14)),
                Nullable(reader, 15, x => x.GetString(15)),
                Nullable(reader, 16, x => x.GetString(16)),
                Nullable(reader, 17, x => (DateTimeOffset?)x.GetFieldValue<DateTimeOffset>(17)),
                reader.GetString(18),
                Nullable(reader, 19, x => (Guid?)x.GetGuid(19)),
                Nullable(reader, 20, x => x.GetString(20)),
                Nullable(reader, 21, x => x.GetString(21)),
                Nullable(reader, 22, x => (Guid?)x.GetGuid(22)),
                reader.GetString(23),
                Nullable(reader, 24, x => (Guid?)x.GetGuid(24)),
                reader.GetGuid(25),
                Nullable(reader, 26, x => (Guid?)x.GetGuid(26)),
                Nullable(reader, 27, x => x.GetString(27))));
        }

        Assert.NotEmpty(records);

        return records;
    }

    private static T? Nullable<T>(NpgsqlDataReader reader, int ordinal, Func<NpgsqlDataReader, T?> read)
        => reader.IsDBNull(ordinal) ? default : read(reader);

    private static async Task<IReadOnlyList<(string EntityType, Guid EntityId, string Role)>>
        ReadReferencesAsync(Guid auditId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type, entity_id, ref_role
            FROM audit.audit_entity_ref
            WHERE audit_id = @auditId
            ORDER BY entity_type, ref_role
            """, connection);

        command.Parameters.AddWithValue("auditId", auditId);

        var references = new List<(string, Guid, string)>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            references.Add((reader.GetString(0), reader.GetGuid(1), reader.GetString(2)));

        return references;
    }

    /// <summary>Every column of one record as text, for absence assertions.</summary>
    private static async Task<string> DumpRecordAsync(Guid auditId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT to_jsonb(r)::text FROM audit.audit_record r WHERE audit_id = @auditId",
            connection);

        command.Parameters.AddWithValue("auditId", auditId);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadTokenHashAsync(UserIdentityId identityId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT token_hash FROM user_token WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountRecordsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_record", connection);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// A created user is the subject of records, never their actor, so
    /// nothing in the trail points at it and it can still be removed.
    /// </summary>
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

    private static string ConnectionString => TestDatabase.ConnectionString;
}
