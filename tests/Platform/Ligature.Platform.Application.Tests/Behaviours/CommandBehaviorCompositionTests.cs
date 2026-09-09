using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
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
                new RecordingAuthorityInitializer(),
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
                new RecordingAuthorityInitializer(),
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
                new RecordingAuthorityInitializer(),
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
                new RecordingAuthorityInitializer(),
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


    // ------------------------------------------------- authority lifetime

    /// <summary>
    /// THE CONTRACT THIS PROVES is not "the context clears on Dispose" — that
    /// is ScopedExecutionContext's own test. It is that AuthorizationBehavior
    /// does not leak an assignment into the next command when the handler
    /// fails.
    ///
    /// A DI scope dispatches several commands. If a failing command left its
    /// authority behind, the next one would execute — and be recorded — under
    /// an authorising assignment that permitted something else. A false audit
    /// record is worse than none, so the release has to survive the exception
    /// path, not merely the happy one.
    /// </summary>
    [Fact]
    public async Task Authority_is_released_when_the_handler_throws()
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

        var authorityInitializer = new RecordingAuthorityInitializer();

        var behavior =
            new AuthorizationBehavior<TestCommand, TestResult>(
                executionContext,
                authorizationService,
                authorityInitializer,
                Clock());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.Handle(
                new TestCommand(),
                CancellationToken.None,
                _ => throw new InvalidOperationException("handler failed")));

        // It WAS established — otherwise this test would pass for the wrong
        // reason, proving only that nothing was ever recorded.
        Assert.NotNull(authorityInitializer.Recorded);

        Assert.False(
            authorityInitializer.Held,
            "The handler threw, so the authority must have been released "
            + "before the exception left AuthorizationBehavior.");
    }

    [Fact]
    public async Task Authority_is_released_when_the_handler_succeeds()
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

        var authorityInitializer = new RecordingAuthorityInitializer();

        var behavior =
            new AuthorizationBehavior<TestCommand, TestResult>(
                executionContext,
                authorizationService,
                authorityInitializer,
                Clock());

        await behavior.Handle(
            new TestCommand(),
            CancellationToken.None,
            _ => Task.FromResult(new TestResult("success")));

        Assert.NotNull(authorityInitializer.Recorded);
        Assert.False(authorityInitializer.Held);
    }

    /// <summary>
    /// A refused command establishes no authority at all, so the eventual
    /// audit record for the refusal carries an identity and no authorising
    /// role — which is exactly what a refusal record should say.
    /// </summary>
    [Fact]
    public async Task A_refused_command_establishes_no_authority()
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

        var authorityInitializer = new RecordingAuthorityInitializer();

        var behavior =
            new AuthorizationBehavior<TestCommand, TestResult>(
                executionContext,
                authorizationService,
                authorityInitializer,
                Clock());

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => behavior.Handle(
                new TestCommand(),
                CancellationToken.None,
                _ => Task.FromResult(new TestResult("unreachable"))));

        Assert.Null(authorityInitializer.Recorded);
        Assert.False(authorityInitializer.Held);
    }

    private static FakeClock Clock()
        => new(new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero));

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

    /// <summary>
    /// Accepts the authority the behaviour records and remembers it, so a test
    /// can assert that an authorised command captured one and a refused
    /// command did not.
    /// </summary>
    private sealed class RecordingAuthorityInitializer : IAuthorityInitializer
    {
        public AuthorizingAssignment? Recorded { get; private set; }

        /// <summary>True while the command that established it is running.</summary>
        public bool Held { get; private set; }

        public IDisposable EstablishAuthority(AuthorizingAssignment authority)
        {
            Recorded = authority;
            Held = true;

            return new Release(this);
        }

        private sealed class Release : IDisposable
        {
            private readonly RecordingAuthorityInitializer _owner;

            internal Release(RecordingAuthorityInitializer owner)
                => _owner = owner;

            public void Dispose() => _owner.Held = false;
        }
    }

    private sealed class FakeExecutionContext
        : IExecutionContext
    {
        public UserId UserId { get; } = UserId.New();

        public ActorType ActorType { get; init; }

        public bool IsAuthenticated { get; init; }

        public ActorIdentity Identity { get; } = TestActorIdentity.Human();

        public AuthorizingAssignment? Authority => null;
    }

    private sealed class FakeAuthorizationService
        : IAuthorizationService
    {
        public bool IsAllowed { get; init; }

        public AuthorizationRequest? LastRequest { get; private set; }

        public Task<AuthorizationResult> IsAllowedAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(
                IsAllowed
                    ? AuthorizationResult.Allowed(
                        new AuthorizingAssignment(
                            RoleId.New(),
                            "Test Role",
                            ScopeType.Global,
                            null,
                            UserRoleId.New()))
                    : AuthorizationResult.Denied);
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