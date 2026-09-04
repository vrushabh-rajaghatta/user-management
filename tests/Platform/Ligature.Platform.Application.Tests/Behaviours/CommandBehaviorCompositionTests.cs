using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Dispatching;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Tests.Behaviors;

public sealed class CommandBehaviorCompositionTests
{
    [Fact]
    public async Task Authorized_human_command_reaches_handler()
    {
        var executionContext = new FakeExecutionContext
        {
            IsAuthenticated = true,
            ActorType = ActorType.Human
        };

        var authorizationService = new FakeAuthorizationService
        {
            IsAllowed = true
        };

        var clock = new FakeClock(
            new DateTimeOffset(
                2026,
                9,
                3,
                10,
                0,
                0,
                TimeSpan.Zero));

        var handler = new FakeHandler();

        var authentication =
            new AuthenticationBehavior<TestCommand, TestResult>(
                executionContext);

        var humanActor =
            new HumanActorBehavior<TestCommand, TestResult>(
                executionContext);

        var authorization =
            new AuthorizationBehavior<TestCommand, TestResult>(
                executionContext,
                authorizationService,
                clock);

        var pipeline = new CommandPipeline();

        var behaviors =
            new ICommandBehavior<TestCommand, TestResult>[]
            {
            authentication,
            humanActor,
            authorization
            };

        var result =
            await pipeline.ExecuteAsync(
                new TestCommand(),
                behaviors,
                handler,
                CancellationToken.None);

        Assert.Equal("success", result.Value);
        Assert.Equal(1, handler.ExecutionCount);

        Assert.NotNull(authorizationService.LastRequest);
        Assert.Equal(
            "test.permission",
            authorizationService.LastRequest!.PermissionCode);

        Assert.Equal(
            executionContext.UserId,
            authorizationService.LastRequest.UserId);

        Assert.Equal(
            clock.UtcNow,
            authorizationService.LastRequest.At);
    }

    [Fact]
    public async Task Unauthenticated_command_does_not_reach_later_behaviors()
    {
        var executionContext = new FakeExecutionContext
        {
            IsAuthenticated = false,
            ActorType = ActorType.Human
        };

        var authorizationService = new FakeAuthorizationService
        {
            IsAllowed = true
        };

        var clock = new FakeClock(DateTimeOffset.UtcNow);

        var handler = new FakeHandler();

        var authentication =
            new AuthenticationBehavior<
                TestCommand,
                TestResult>(
                executionContext);

        var humanActor =
            new HumanActorBehavior<
                TestCommand,
                TestResult>(
                executionContext);

        var authorization =
            new AuthorizationBehavior<
                TestCommand,
                TestResult>(
                executionContext,
                authorizationService,
                clock);

        var pipeline = new CommandPipeline();

        var behaviors =
            new ICommandBehavior<TestCommand, TestResult>[]
            {
                authentication,
                humanActor,
                authorization
            };

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () =>
                pipeline.ExecuteAsync(
                    new TestCommand(),
                    behaviors,
                    handler,
                    CancellationToken.None));

        Assert.Equal(0, handler.ExecutionCount);
        Assert.Null(authorizationService.LastRequest);
    }

    [Fact]
    public async Task Non_human_actor_does_not_reach_authorization()
    {
        var executionContext = new FakeExecutionContext
        {
            IsAuthenticated = true,
            ActorType = ActorType.Agent
        };

        var authorizationService = new FakeAuthorizationService
        {
            IsAllowed = true
        };

        var clock = new FakeClock(DateTimeOffset.UtcNow);

        var handler = new FakeHandler();

        var authentication =
            new AuthenticationBehavior<
                TestCommand,
                TestResult>(
                executionContext);

        var humanActor =
            new HumanActorBehavior<
                TestCommand,
                TestResult>(
                executionContext);

        var authorization =
            new AuthorizationBehavior<
                TestCommand,
                TestResult>(
                executionContext,
                authorizationService,
                clock);

        var pipeline = new CommandPipeline();

        var behaviors =
            new ICommandBehavior<TestCommand, TestResult>[]
            {
                authentication,
                humanActor,
                authorization
            };

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () =>
                pipeline.ExecuteAsync(
                    new TestCommand(),
                    behaviors,
                    handler,
                    CancellationToken.None));

        Assert.Equal(0, handler.ExecutionCount);
        Assert.Null(authorizationService.LastRequest);
    }

    [Fact]
    public async Task Unauthorized_command_does_not_reach_handler()
    {
        var executionContext = new FakeExecutionContext
        {
            IsAuthenticated = true,
            ActorType = ActorType.Human
        };

        var authorizationService = new FakeAuthorizationService
        {
            IsAllowed = false
        };

        var clock = new FakeClock(DateTimeOffset.UtcNow);

        var handler = new FakeHandler();

        var authentication =
            new AuthenticationBehavior<
                TestCommand,
                TestResult>(
                executionContext);

        var humanActor =
            new HumanActorBehavior<
                TestCommand,
                TestResult>(
                executionContext);

        var authorization =
            new AuthorizationBehavior<
                TestCommand,
                TestResult>(
                executionContext,
                authorizationService,
                clock);

        var pipeline = new CommandPipeline();

        var behaviors =
            new ICommandBehavior<TestCommand, TestResult>[]
            {
                authentication,
                humanActor,
                authorization
            };

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () =>
                pipeline.ExecuteAsync(
                    new TestCommand(),
                    behaviors,
                    handler,
                    CancellationToken.None));

        Assert.Equal(0, handler.ExecutionCount);
    }

    private sealed record TestCommand
        : IAuthorizableCommand<TestResult>,
          IHumanActorOnlyCommand<TestResult>
    {
        public string RequiredPermission => "test.permission";
    }

    private sealed record TestResult(string Value);

    private sealed class FakeHandler
        : ICommandHandler<TestCommand, TestResult>
    {
        public int ExecutionCount { get; private set; }

        public Task<TestResult> Handle(
    TestCommand command,
    CancellationToken cancellationToken)
        {
            ExecutionCount++;

            return Task.FromResult(
                new TestResult("success"));
        }
    }

    private sealed class FakeExecutionContext
        : IExecutionContext
    {
        public UserId UserId { get; } = UserId.New();

        public ActorType ActorType { get; init; }

        public bool IsAuthenticated { get; init; }
    }

    private sealed class FakeAuthorizationService
        : IAuthorizationService
    {
        public bool IsAllowed { get; init; }

        public AuthorizationRequest? LastRequest { get; private set; }

        public Task<bool> IsAllowedAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(IsAllowed);
        }
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}