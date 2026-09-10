using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Tests.Behaviours;

/// <summary>
/// Behaviours 6 and 7 composed, with the database replaced by a recording
/// writer. What is asserted here is the pipeline's own contract: a command
/// that declares nothing writes nothing, a command that declares gets one
/// operation and one actor snapshot across every record, and any refusal
/// leaves the transaction to roll back rather than writing a record that
/// fails validation.
///
/// The registered command type is CreateUserCommand because IMPL-08's
/// registry is static: a locally declared test command is, correctly, not
/// registered — which is one of the cases below.
/// </summary>
public sealed class AuditEmissionBehaviorTests
{
    private static readonly DateTimeOffset Opened = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_command_that_declares_nothing_writes_nothing()
    {
        var writer = new RecordingWriter();
        var events = new ScopedAuditEvents();

        using var open = ((IAuditEmissionScope)events).BeginCommand(Guid.NewGuid(), Opened);

        await Emission(events, writer).Handle(
            Command(), CancellationToken.None, _ => Task.FromResult(Result()));

        Assert.Null(writer.Rows);
    }

    [Fact]
    public async Task Every_record_of_one_command_carries_one_operation_and_one_actor()
    {
        var writer = new RecordingWriter();
        var events = new ScopedAuditEvents();
        var operationId = Guid.NewGuid();

        using var open = ((IAuditEmissionScope)events).BeginCommand(operationId, Opened);

        await Emission(events, writer).Handle(
            Command(),
            CancellationToken.None,
            _ =>
            {
                ((IAuditEvents)events).Emit("UserCreated", 1)
                    .Primary("User", Guid.NewGuid())
                    .WithAfter(new { FirstName = "Grace" });

                ((IAuditEvents)events).Emit("UserCreated", 1)
                    .Primary("User", Guid.NewGuid())
                    .WithAfter(new { FirstName = "Ada" });

                return Task.FromResult(Result());
            });

        Assert.NotNull(writer.Rows);
        Assert.Equal(2, writer.Rows!.Count);

        Assert.All(writer.Rows, row =>
        {
            Assert.Equal(operationId, row.OperationId);
            Assert.Equal("Test Person", row.Actor.DisplayName);
            Assert.Equal("Authenticated", row.Actor.OriginKind);

            // Behaviour 16: OccurredAt is the command's, CapturedAt is now.
            Assert.Equal(Opened, row.OccurredAt);
            Assert.Equal(Now, row.CapturedAt);
        });
    }

    /// <summary>
    /// The declarations of an attempt that rolled back are evidence of
    /// nothing. Behaviour 7 clears them before the handler runs, so a
    /// replayed unit of work writes only what its own attempt declared.
    /// </summary>
    [Fact]
    public async Task A_replayed_attempt_does_not_carry_the_previous_attempts_declarations()
    {
        var writer = new RecordingWriter();
        var events = new ScopedAuditEvents();

        using var open = ((IAuditEmissionScope)events).BeginCommand(Guid.NewGuid(), Opened);

        // The attempt that rolled back.
        ((IAuditEvents)events).Emit("UserCreated", 1)
            .Primary("User", Guid.NewGuid())
            .WithAfter(new { FirstName = "Discarded" });

        await Emission(events, writer).Handle(
            Command(),
            CancellationToken.None,
            _ =>
            {
                ((IAuditEvents)events).Emit("UserCreated", 1)
                    .Primary("User", Guid.NewGuid())
                    .WithAfter(new { FirstName = "Committed" });

                return Task.FromResult(Result());
            });

        var row = Assert.Single(writer.Rows!);

        Assert.Contains("Committed", row.After);
    }

    [Fact]
    public async Task A_command_that_declares_without_being_registered_is_a_defect()
    {
        var writer = new RecordingWriter();
        var events = new ScopedAuditEvents();

        using var open = ((IAuditEmissionScope)events).BeginCommand(Guid.NewGuid(), Opened);

        var behavior = new AuditEmissionBehavior<UnregisteredCommand, CreateUserResult>(
            events, Context(), Catalogue(), writer, new FixedClock());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.Handle(
                new UnregisteredCommand(),
                CancellationToken.None,
                _ =>
                {
                    ((IAuditEvents)events).Emit("UserCreated", 1)
                        .Primary("User", Guid.NewGuid())
                        .WithAfter(new { FirstName = "Ada" });

                    return Task.FromResult(Result());
                }));

        Assert.Contains("not registered in AuditDeclarations", failure.Message);
        Assert.Contains("IMPL-08", failure.Message);
        Assert.Null(writer.Rows);
    }

    /// <summary>
    /// A validation refusal must reach the transaction, not the writer. The
    /// exception is what rolls the command back; a record that fails
    /// validation is never written in a weakened form.
    /// </summary>
    [Fact]
    public async Task A_refused_declaration_propagates_and_nothing_is_written()
    {
        var writer = new RecordingWriter();
        var events = new ScopedAuditEvents();

        using var open = ((IAuditEmissionScope)events).BeginCommand(Guid.NewGuid(), Opened);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Emission(events, writer).Handle(
                Command(),
                CancellationToken.None,
                _ =>
                {
                    // UserCreated is a BeforeAfter event; a payload is a defect.
                    ((IAuditEvents)events).Emit("UserCreated", 1)
                        .Primary("User", Guid.NewGuid())
                        .WithPayload(new { anything = 1 });

                    return Task.FromResult(Result());
                }));

        Assert.Contains("Audit emission defect", failure.Message);
        Assert.Null(writer.Rows);
    }

    /// <summary>
    /// The handler's own failure reaches the caller unchanged: nothing is
    /// written, and the audit pipeline does not become the reason a domain
    /// error looks like a defect.
    /// </summary>
    [Fact]
    public async Task A_failing_handler_writes_nothing_and_keeps_its_own_exception()
    {
        var writer = new RecordingWriter();
        var events = new ScopedAuditEvents();

        using var open = ((IAuditEmissionScope)events).BeginCommand(Guid.NewGuid(), Opened);

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => Emission(events, writer).Handle(
                Command(),
                CancellationToken.None,
                _ =>
                {
                    ((IAuditEvents)events).Emit("UserCreated", 1)
                        .Primary("User", Guid.NewGuid())
                        .WithAfter(new { FirstName = "Ada" });

                    throw new InvalidTimeZoneException("the handler failed");
                }));

        Assert.Null(writer.Rows);
    }

    // --------------------------------------------- the audit command scope

    [Fact]
    public async Task The_command_scope_opens_the_command_and_closes_it_again()
    {
        var events = new ScopedAuditEvents();
        var scope = (IAuditEmissionScope)events;

        Guid observed = default;

        await Scope(events, new RecordingAutonomousWriter())
            .Handle(Activation(), CancellationToken.None, _ =>
            {
                observed = scope.OperationId;

                Assert.Equal(Now, scope.OccurredAt);

                return Task.FromResult(Activated());
            });

        Assert.NotEqual(default, observed);

        // Closed on the way out: a later command in the same DI scope gets
        // its own operation rather than inheriting this one.
        Assert.Throws<InvalidOperationException>(() => scope.OperationId);
    }

    [Fact]
    public async Task The_command_scope_closes_the_command_even_when_it_fails()
    {
        var events = new ScopedAuditEvents();

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => Scope(events, new RecordingAutonomousWriter())
                .Handle(Activation(), CancellationToken.None,
                    _ => throw new InvalidTimeZoneException("the handler failed")));

        Assert.Throws<InvalidOperationException>(
            () => ((IAuditEmissionScope)events).OperationId);
    }

    /// <summary>
    /// The property the whole autonomous path exists for. The command failed,
    /// its transaction rolled back, and the record of that failure stands.
    /// </summary>
    [Fact]
    public async Task An_autonomous_record_survives_a_command_that_failed()
    {
        var events = new ScopedAuditEvents();
        var writer = new RecordingAutonomousWriter();

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => Scope(events, writer).Handle(
                Activation(), CancellationToken.None,
                _ =>
                {
                    DeclareRejection(events);

                    throw new InvalidTimeZoneException("the handler failed");
                }));

        var row = Assert.Single(writer.Rows!);

        Assert.Equal("TokenRejected", row.EventType);
        Assert.Equal("Autonomous", row.WritePath);
    }

    /// <summary>
    /// Routing is the catalogue's, not the handler's: this behaviour takes
    /// only what the catalogue marks Autonomous, and leaves the rest to the
    /// behaviour inside the transaction.
    /// </summary>
    [Fact]
    public async Task The_command_scope_writes_only_the_autonomous_declarations()
    {
        var events = new ScopedAuditEvents();
        var writer = new RecordingAutonomousWriter();

        await Scope(events, writer).Handle(
            Activation(), CancellationToken.None,
            _ =>
            {
                ((IAuditEvents)events).Emit("AccountActivated", 1)
                    .Primary("Identity", Guid.NewGuid());

                DeclareRejection(events);

                return Task.FromResult(Activated());
            });

        Assert.Equal(["TokenRejected"], writer.Rows!.Select(x => x.EventType));
    }

    [Fact]
    public async Task A_command_declaring_nothing_autonomous_writes_nothing_here()
    {
        var events = new ScopedAuditEvents();
        var writer = new RecordingAutonomousWriter();

        await Scope(events, writer).Handle(
            Activation(), CancellationToken.None,
            _ =>
            {
                ((IAuditEvents)events).Emit("AccountActivated", 1)
                    .Primary("Identity", Guid.NewGuid());

                return Task.FromResult(Activated());
            });

        Assert.Null(writer.Rows);
    }

    // ------------------------------------------------- failure semantics

    /// <summary>
    /// The command succeeded and its change is committed; the record of it
    /// could not be written. The request fails. Nobody is told an action was
    /// recorded when it was not, and the operator learns immediately.
    /// </summary>
    [Fact]
    public async Task A_failed_autonomous_write_fails_a_command_that_succeeded()
    {
        var events = new ScopedAuditEvents();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Scope(events, new ThrowingAutonomousWriter()).Handle(
                Activation(), CancellationToken.None,
                _ =>
                {
                    DeclareRejection(events);

                    return Task.FromResult(Activated());
                }));

        Assert.Contains("could not be written", failure.Message);

        Assert.Contains("the autonomous writer failed", failure.InnerException!.Message);
    }

    /// <summary>
    /// The command failed and the audit write failed too. The command's own
    /// exception is what explains the request, so it survives unchanged —
    /// its type decides the response, and a 400 must not become a 500 because
    /// the trail was unavailable. The audit failure travels with it.
    /// </summary>
    [Fact]
    public async Task A_failed_autonomous_write_does_not_mask_the_commands_failure()
    {
        var events = new ScopedAuditEvents();

        var failure = await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => Scope(events, new ThrowingAutonomousWriter()).Handle(
                Activation(), CancellationToken.None,
                _ =>
                {
                    DeclareRejection(events);

                    throw new InvalidTimeZoneException("the handler failed");
                }));

        Assert.Equal("the handler failed", failure.Message);

        var attached = Assert.IsType<string>(
            failure.Data[AuditCommandScopeBehavior<ActivateAccountCommand, ActivateAccountResult>.AuditFailureKey]);

        Assert.Contains("the autonomous writer failed", attached);
    }

    // ---------------------------------------------------------- harness

    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 5, TimeSpan.Zero);

    private static AuditEmissionBehavior<CreateUserCommand, CreateUserResult> Emission(
        ScopedAuditEvents events, IAuditRecordWriter writer)
        => new(events, Context(), Catalogue(), writer, new FixedClock());

    private static AuditCommandScopeBehavior<ActivateAccountCommand, ActivateAccountResult> Scope(
        ScopedAuditEvents events, IAutonomousAuditRecordWriter writer)
        => new(events, Context(), Catalogue(), writer, new FixedClock());

    private static ActivateAccountCommand Activation()
        => new("token-plaintext", "a-sufficiently-long-password");

    private static ActivateAccountResult Activated()
        => new(UserIdentityId.New());

    /// <summary>An autonomous, anonymous-origin declaration.</summary>
    private static void DeclareRejection(ScopedAuditEvents events)
        => ((IAuditEvents)events).Emit("TokenRejected", 1)
            .Primary("Token", Guid.NewGuid())
            .WithPayload(new { reason = "Invalid" });

    private static CreateUserCommand Command()
        => new("Grace", "Hopper", "Grace Hopper", "grace@example.test", "grace.hopper");

    private static CreateUserResult Result()
        => new(UserId.New(), UserIdentityId.New());

    private static IExecutionContext Context()
    {
        var context = new ScopedExecutionContext();

        context.Establish(UserId.New(), ActorType.Human, TestActorIdentity.Human());

        return context;
    }

    private static IAuditEventCatalogue Catalogue()
        => new AuditEventCatalogueSnapshot(
        [
            new AuditEventTypeDefinition(
                "UserCreated", 1, "UserManagement", "IdentityLifecycle", ReasonRequired: false,
                "Transactional", "BeforeAfter", "User", PrimaryEntityRequired: true,
                [], [], IsActive: true,
                new Dictionary<string, bool> { ["Authenticated"] = true }),

            new AuditEventTypeDefinition(
                "AccountActivated", 1, "UserManagement", "IdentityLifecycle", ReasonRequired: false,
                "Transactional", "None", "Identity", PrimaryEntityRequired: true,
                [], [], IsActive: true,
                new Dictionary<string, bool> { ["Authenticated"] = true }),

            new AuditEventTypeDefinition(
                "TokenRejected", 1, "UserManagement", "SecurityEvent", ReasonRequired: false,
                "Autonomous", "Payload", "Token", PrimaryEntityRequired: false,
                [], [], IsActive: true,
                new Dictionary<string, bool> { ["Authenticated"] = true, ["Anonymous"] = true }),
        ]);

    private sealed record UnregisteredCommand : ICommand<CreateUserResult>;

    private sealed class RecordingWriter : IAuditRecordWriter
    {
        public IReadOnlyList<AuditRecordRow>? Rows { get; private set; }

        public Task WriteAsync(
            IReadOnlyList<AuditRecordRow> rows, CancellationToken cancellationToken)
        {
            Rows = rows;

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAutonomousWriter : IAutonomousAuditRecordWriter
    {
        public IReadOnlyList<AuditRecordRow>? Rows { get; private set; }

        public Task WriteAsync(
            IReadOnlyList<AuditRecordRow> rows, CancellationToken cancellationToken)
        {
            Rows = rows;

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAutonomousWriter : IAutonomousAuditRecordWriter
    {
        public Task WriteAsync(
            IReadOnlyList<AuditRecordRow> rows, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Audit emission defect — the autonomous writer failed.");
    }

    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
            => operation(cancellationToken);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
