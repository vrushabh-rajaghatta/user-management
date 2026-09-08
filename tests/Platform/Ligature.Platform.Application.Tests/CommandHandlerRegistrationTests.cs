using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Dispatching;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Tests;

/// <summary>
/// CommandDispatcher resolves handlers with GetRequiredService, so a missing
/// registration is not a compile error — it is a runtime failure on the first
/// dispatch of that command. Handlers are registered explicitly rather than by
/// assembly scanning, which makes "is this command wired in?" a question these
/// tests can answer directly.
/// </summary>
public sealed class CommandHandlerRegistrationTests
{
    [Fact]
    public void The_CreateUser_handler_is_registered()
    {
        var descriptor = new ServiceCollection()
            .AddPlatformApplication()
            .SingleOrDefault(x =>
                x.ServiceType ==
                    typeof(ICommandHandler<CreateUserCommand, CreateUserResult>));

        Assert.NotNull(descriptor);

        Assert.Equal(
            typeof(CreateUserCommandHandler),
            descriptor!.ImplementationType);

        // Scoped, so the handler shares the request's unit of work and
        // execution context rather than outliving them.
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>
    /// Resolution is what the dispatcher actually does, and it fails for
    /// reasons a descriptor check cannot see — an unregistered dependency of
    /// the handler, for instance.
    /// </summary>
    [Fact]
    public void The_dispatcher_can_resolve_the_CreateUser_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateUserCommand, CreateUserResult>>();

        Assert.IsType<CreateUserCommandHandler>(handler);

        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<ICommandDispatcher>());
    }

    /// <summary>
    /// Adding handler registration must not have displaced what was already
    /// there — the pipeline, the dispatcher, all three behaviours, and both
    /// sides of the execution context.
    /// </summary>
    [Fact]
    public void The_existing_application_registrations_are_intact()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CommandPipeline>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICommandDispatcher>());

        var behaviors = scope.ServiceProvider
            .GetServices<ICommandBehavior<CreateUserCommand, CreateUserResult>>()
            .ToList();

        Assert.Equal(3, behaviors.Count);

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IExecutionContext>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IExecutionContextInitializer>());
    }

    /// <summary>
    /// The handler's own dependencies are persistence concerns, supplied by
    /// AddPlatformPersistence. Referencing that project from the Application
    /// test assembly would invert the dependency, so they are stubbed here —
    /// this asserts the Application-side wiring, not the composition of the
    /// whole system.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection().AddPlatformApplication();

        services.AddScoped<IUnitOfWork, StubUnitOfWork>();
        services.AddScoped<IUserRepository, StubUserRepository>();
        services.AddScoped<IUserIdentityRepository, StubUserIdentityRepository>();
        services.AddScoped<IUserTokenRepository, StubUserTokenRepository>();
        services.AddScoped<ISecurityPolicyResolver, StubSecurityPolicyResolver>();
        services.AddScoped<IUserTokenService, StubUserTokenService>();
        services.AddScoped<IAuthorizationService, StubAuthorizationService>();
        services.AddScoped<IClock, StubClock>();
        services.AddScoped<IPasswordHasher, StubPasswordHasher>();
        services.AddScoped<ICredentialRepository, StubCredentialRepository>();
        services.AddScoped<IPasswordHistoryRepository, StubPasswordHistoryRepository>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
            => operation(cancellationToken);
    }

    private sealed class StubUserRepository : IUserRepository
    {
        public Task<bool> ExistsActiveHumanWithEmailAsync(
            Domain.Users.EmailAddress email, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(Domain.Users.User user, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubUserIdentityRepository : IUserIdentityRepository
    {
        public Task<bool> ExistsWithUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(
            Domain.Users.UserIdentity identity, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubUserTokenRepository : IUserTokenRepository
    {
        public Task AddAsync(
            Domain.Users.UserToken token, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Domain.Users.UserIdentityId?> TryConsumeAsync(
            Domain.Users.UserTokenId tokenId, string tokenHash,
            DateTimeOffset now, CancellationToken cancellationToken)
            => Task.FromResult<Domain.Users.UserIdentityId?>(null);
    }

    private sealed class StubSecurityPolicyResolver : ISecurityPolicyResolver
    {
        public Task<Domain.Users.SecurityPolicySettings> GetEffectiveSettingsAsync(
            DateTimeOffset at, CancellationToken cancellationToken)
            => Task.FromResult(Domain.Users.SecurityBaseline.Current);
    }

    private sealed class StubUserTokenService : IUserTokenService
    {
        public TokenMaterial Generate(Domain.Users.UserTokenId tokenId)
            => new($"{tokenId.Value}.secret", "hash");

        public string Hash(string secret) => "hash";

        public PresentedToken? Parse(string plainText) => null;
    }

    private sealed class StubAuthorizationService : IAuthorizationService
    {
        public Task<bool> IsAllowedAsync(
            AuthorizationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public PasswordHashMaterial Hash(string password)
            => new("hash", "algorithm");
    }

    private sealed class StubCredentialRepository : ICredentialRepository
    {
        public Task AddAsync(
            Domain.Users.Credential credential, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubPasswordHistoryRepository : IPasswordHistoryRepository
    {
        public Task AddAsync(
            Domain.Users.PasswordHistory history, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 9, 7, 13, 0, 0, TimeSpan.Zero);
    }
}
