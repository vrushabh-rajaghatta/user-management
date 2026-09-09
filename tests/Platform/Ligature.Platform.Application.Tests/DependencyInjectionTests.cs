using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Dispatching;
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

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow
            => new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);
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
                    behavior));
    }

    private sealed record TestCommand
        : ICommand<TestResult>;

    private sealed record TestResult;
}