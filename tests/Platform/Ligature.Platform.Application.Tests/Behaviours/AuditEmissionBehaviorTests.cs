using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Execution;
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

    // ------------------------------------------------------- behaviour 6

    [Fact]
    public async Task The_transaction_scope_opens_the_command_and_closes_it_again()
    {
        var events = new ScopedAuditEvents();
        var scope = (IAuditEmissionScope)events;

        Guid observed = default;

        await new TransactionScopeBehavior<CreateUserCommand, CreateUserResult>(
            new PassThroughUnitOfWork(), events, new FixedClock())
            .Handle(Command(), CancellationToken.None, _ =>
            {
                observed = scope.OperationId;

                Assert.Equal(Now, scope.OccurredAt);

                return Task.FromResult(Result());
            });

        Assert.NotEqual(default, observed);

        // Closed on the way out: a later command in the same DI scope gets
        // its own operation rather than inheriting this one.
        Assert.Throws<InvalidOperationException>(() => scope.OperationId);
    }

    [Fact]
    public async Task The_transaction_scope_closes_the_command_even_when_it_fails()
    {
        var events = new ScopedAuditEvents();

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => new TransactionScopeBehavior<CreateUserCommand, CreateUserResult>(
                new PassThroughUnitOfWork(), events, new FixedClock())
                .Handle(Command(), CancellationToken.None,
                    _ => throw new InvalidTimeZoneException("the handler failed")));

        Assert.Throws<InvalidOperationException>(
            () => ((IAuditEmissionScope)events).OperationId);
    }

    // ---------------------------------------------------------- harness

    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 5, TimeSpan.Zero);

    private static AuditEmissionBehavior<CreateUserCommand, CreateUserResult> Emission(
        ScopedAuditEvents events, IAuditRecordWriter writer)
        => new(events, Context(), Catalogue(), writer, new FixedClock());

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
