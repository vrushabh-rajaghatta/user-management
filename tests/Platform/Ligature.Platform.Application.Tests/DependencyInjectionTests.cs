using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Dispatching;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Tests;

public sealed class DependencyInjectionTests
{
    private sealed class FakeExecutionContext : IExecutionContext
    {
        public UserId UserId { get; } = UserId.New();

        public ActorType ActorType
            => ActorType.Human;

        public bool IsAuthenticated
            => true;

        public ActorIdentity Identity
            => TestActorIdentity.Human();

        public AuthorizingAssignment? Authority
            => null;
    }

    private sealed class FakeAuthorizationService
        : IAuthorizationService
    {
        public Task<AuthorizationResult> IsAllowedAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                AuthorizationResult.Allowed(
                    new AuthorizingAssignment(
                        RoleId.New(),
                        "Test Role",
                        ScopeType.Global,
                        null,
                        UserRoleId.New())));
        }
    }

    private sealed class FakeNotificationRepository : INotificationRepository
    {
        public Task AddAsync(
            Notification notification,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeAutonomousWriter : IAutonomousAuditRecordWriter
    {
        public Task WriteAsync(
            IReadOnlyList<AuditRecordRow> rows, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow
            => new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Behaviour 6 opens the command's transaction through the unit of work,
    /// which is a persistence concern; this runs the delegate directly.
    /// </summary>
    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
            => operation(cancellationToken);
    }

    private sealed class FakeCatalogue : IAuditEventCatalogue
    {
        public AuditEventTypeDefinition? Find(string code, int version)
            => null;

        public IReadOnlyCollection<AuditEventTypeDefinition> All
            => [];
    }

    private sealed class FakeAuditRecordWriter : IAuditRecordWriter
    {
        public Task WriteAsync(
            IReadOnlyList<AuditRecordRow> rows,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Fact]
    public void Application_services_register_command_pipeline()
    {
        var services = new ServiceCollection();

        services.AddPlatformApplication();

        services.AddScoped<
            IExecutionContext,
            FakeExecutionContext>();

        services.AddScoped<
            IAuthorizationService,
            FakeAuthorizationService>();

        services.AddScoped<
            IClock,
            FakeClock>();

        services.AddScoped<
            IUnitOfWork,
            FakeUnitOfWork>();

        services.AddScoped<
            IAuditEventCatalogue,
            FakeCatalogue>();

        services.AddScoped<
            IAuditRecordWriter,
            FakeAuditRecordWriter>();

        services.AddScoped<
            IAutonomousAuditRecordWriter,
            FakeAutonomousWriter>();

        services.AddScoped<
            INotificationRepository,
            FakeNotificationRepository>();

        using var provider = services.BuildServiceProvider();

        var pipeline =
            provider.GetService<CommandPipeline>();

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void Application_services_register_behaviors_in_expected_order()
    {
        var services = new ServiceCollection();

        services.AddPlatformApplication();

        services.AddScoped<
            IExecutionContext,
            FakeExecutionContext>();

        services.AddScoped<
            IAuthorizationService,
            FakeAuthorizationService>();

        services.AddScoped<
            IClock,
            FakeClock>();

        services.AddScoped<
            IUnitOfWork,
            FakeUnitOfWork>();

        services.AddScoped<
            IAuditEventCatalogue,
            FakeCatalogue>();

        services.AddScoped<
            IAuditRecordWriter,
            FakeAuditRecordWriter>();

        services.AddScoped<
            IAutonomousAuditRecordWriter,
            FakeAutonomousWriter>();

        services.AddScoped<
            INotificationRepository,
            FakeNotificationRepository>();

        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();

        var behaviors =
            scope.ServiceProvider
                .GetServices<
                    ICommandBehavior<TestCommand, TestResult>>();

        Assert.Collection(
            behaviors,
            behavior =>
                Assert.IsType<
                    AuthenticationBehavior<TestCommand, TestResult>>(
                    behavior),

            behavior =>
                Assert.IsType<
                    HumanActorBehavior<TestCommand, TestResult>>(
                    behavior),

            behavior =>
                Assert.IsType<
                    AuthorizationBehavior<TestCommand, TestResult>>(
                    behavior),

            // Notification's post-commit behaviour wraps the audit command
            // scope, so a command whose autonomous audit write failed after
            // commit — which fails the request — hands off no notification.
            behavior =>
                Assert.IsType<
                    NotificationPostCommitBehavior<TestCommand, TestResult>>(
                    behavior),

            // The audit command scope wraps the transaction, because what it
            // writes must outlive the transaction's fate. Behaviour 6 then
            // opens the transaction that behaviour 7 writes inside, so it
            // must wrap 7 — and all three sit inside authorisation, so a
            // refused command never reaches any of them.
            behavior =>
                Assert.IsType<
                    AuditCommandScopeBehavior<TestCommand, TestResult>>(
                    behavior),

            behavior =>
                Assert.IsType<
                    TransactionScopeBehavior<TestCommand, TestResult>>(
                    behavior),

            // Inside the transaction, and outside behaviour 7: the clear must
            // run once per ATTEMPT, which only something inside the execution
            // strategy's delegate can do.
            behavior =>
                Assert.IsType<
                    NotificationEmissionBehavior<TestCommand, TestResult>>(
                    behavior),

            behavior =>
                Assert.IsType<
                    AuditEmissionBehavior<TestCommand, TestResult>>(
                    behavior));
    }

    private sealed record TestCommand
        : ICommand<TestResult>;

    private sealed record TestResult;
}