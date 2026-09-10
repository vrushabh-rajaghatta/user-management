using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
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

        // Three authorisation-side behaviours, plus the audit command scope,
        // the transaction scope inside it, and the audit emission inside that.
        Assert.Equal(6, behaviors.Count);

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
        services.AddScoped<IUserSessionRepository, StubUserSessionRepository>();
        services.AddScoped<IAuditEventCatalogue, StubAuditEventCatalogue>();
        services.AddScoped<IAuditRecordWriter, StubAuditRecordWriter>();
        services.AddScoped<IAutonomousAuditRecordWriter, StubAutonomousWriter>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// Behaviour 7's collaborators are persistence concerns like the rest:
    /// the catalogue is loaded from the audit schema and the writer inserts
    /// into it. Neither is exercised here — the command declares nothing when
    /// the handler never runs.
    /// </summary>
    private sealed class StubAuditEventCatalogue : IAuditEventCatalogue
    {
        public AuditEventTypeDefinition? Find(string code, int version)
            => null;

        public IReadOnlyCollection<AuditEventTypeDefinition> All
            => [];
    }

    private sealed class StubAutonomousWriter : IAutonomousAuditRecordWriter
    {
        public Task WriteAsync(
            IReadOnlyList<AuditRecordRow> rows, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubAuditRecordWriter : IAuditRecordWriter
    {
        public Task WriteAsync(
            IReadOnlyList<AuditRecordRow> rows,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
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

        public Task<Domain.Users.User?> FindAsync(
            Domain.Users.UserId userId, CancellationToken cancellationToken)
            => Task.FromResult<Domain.Users.User?>(null);
    }

    private sealed class StubUserIdentityRepository : IUserIdentityRepository
    {
        public Task<bool> ExistsWithUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(
            Domain.Users.UserIdentity identity, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Domain.Users.UserIdentity?> FindLocalByUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult<Domain.Users.UserIdentity?>(null);

        public Task<Domain.Users.UserIdentity?> FindAsync(
            Domain.Users.UserIdentityId userIdentityId,
            CancellationToken cancellationToken)
            => Task.FromResult<Domain.Users.UserIdentity?>(null);
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
        public Task<AuthorizationResult> IsAllowedAsync(
            AuthorizationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(
                AuthorizationResult.Allowed(
                    new AuthorizingAssignment(
                        RoleId.New(),
                        "Test Role",
                        ScopeType.Global,
                        null,
                        UserRoleId.New())));
    }

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public PasswordHashMaterial Hash(string password)
            => new("hash", "algorithm");

        public PasswordVerificationResult Verify(
            string password, string storedHash, string storedAlgorithm)
            => new(false, false);

        public void VerifyDecoy(string password)
        {
        }
    }

    private sealed class StubCredentialRepository : ICredentialRepository
    {
        public Task AddAsync(
            Domain.Users.Credential credential, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Domain.Users.Credential?> FindByIdentityAsync(
            Domain.Users.UserIdentityId userIdentityId,
            CancellationToken cancellationToken)
            => Task.FromResult<Domain.Users.Credential?>(null);
    }

    private sealed class StubPasswordHistoryRepository : IPasswordHistoryRepository
    {
        public Task AddAsync(
            Domain.Users.PasswordHistory history, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubUserSessionRepository : IUserSessionRepository
    {
        public Task AddAsync(
            Domain.Users.UserSession session, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Domain.Users.UserSession?> FindAsync(
            Domain.Users.UserSessionId sessionId,
            CancellationToken cancellationToken)
            => Task.FromResult<Domain.Users.UserSession?>(null);

        public Task RecordActivityAsync(
            Domain.Users.UserSessionId sessionId,
            DateTimeOffset now,
            TimeSpan staleness,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 9, 7, 13, 0, 0, TimeSpan.Zero);
    }
}
