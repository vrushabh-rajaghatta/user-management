using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Application.Dispatching;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.AdminResetPassword;
using Ligature.Platform.Application.Users.Commands.ChangePassword;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Commands.DeactivateUser;
using Ligature.Platform.Application.Users.Commands.GrantRole;
using Ligature.Platform.Application.Users.Commands.ReactivateUser;
using Ligature.Platform.Application.Users.Commands.ReissueActivationLink;
using Ligature.Platform.Application.Users.Commands.RevokeRole;
using Ligature.Platform.Application.Users.Commands.RevokeSession;
using Ligature.Platform.Application.Users.Commands.RevokeUserSessions;
using Ligature.Platform.Application.Users.Commands.SignOutEverywhere;
using Ligature.Platform.Application.Users.Commands.UnlockAccount;
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
    /// SES-C3 and both SES-C4 commands, resolved for the same reason as CRD-C5
    /// below.
    /// </summary>
    [Fact]
    public void The_dispatcher_can_resolve_the_session_revocation_handlers()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<RevokeSessionCommandHandler>(scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RevokeSessionCommand, RevokeSessionResult>>());

        Assert.IsType<RevokeUserSessionsCommandHandler>(scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RevokeUserSessionsCommand, RevokeUserSessionsResult>>());

        Assert.IsType<SignOutEverywhereCommandHandler>(scope.ServiceProvider
            .GetRequiredService<ICommandHandler<SignOutEverywhereCommand, SignOutEverywhereResult>>());
    }

    /// <summary>
    /// CRD-C6, resolved for the same reason as CRD-C5 below.
    /// </summary>
    [Fact]
    public void The_dispatcher_can_resolve_the_UnlockAccount_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<UnlockAccountCommand, UnlockAccountResult>>();

        Assert.IsType<UnlockAccountCommandHandler>(handler);
    }

    /// <summary>
    /// CRD-C4, resolved for the same reason as CRD-C5 below.
    /// </summary>
    [Fact]
    public void The_dispatcher_can_resolve_the_ChangePassword_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ChangePasswordCommand, ChangePasswordResult>>();

        Assert.IsType<ChangePasswordCommandHandler>(handler);
    }

    /// <summary>
    /// CRD-C5. Resolved rather than only described, so a dependency the
    /// handler takes but nothing registers fails here and not on the first
    /// administrator's request.
    /// </summary>
    [Fact]
    public void The_dispatcher_can_resolve_the_AdminResetPassword_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<AdminResetPasswordCommand, AdminResetPasswordResult>>();

        Assert.IsType<AdminResetPasswordCommandHandler>(handler);
    }

    /// <summary>
    /// CRD-C7. Resolved, for CRD-C5's reason: a dependency the handler takes
    /// but nothing registers fails here, not on the first administrator's
    /// request.
    /// </summary>
    [Fact]
    public void The_dispatcher_can_resolve_the_ReissueActivationLink_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReissueActivationLinkCommand, ReissueActivationLinkResult>>();

        Assert.IsType<ReissueActivationLinkCommandHandler>(handler);
    }

    /// <summary>AUT-C1, resolved for CRD-C5's reason.</summary>
    [Fact]
    public void The_dispatcher_can_resolve_the_GrantRole_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<GrantRoleCommand, GrantRoleResult>>();

        Assert.IsType<GrantRoleCommandHandler>(handler);
    }

    /// <summary>USR-C4 and USR-C5, resolved for CRD-C5's reason.</summary>
    [Fact]
    public void The_dispatcher_can_resolve_the_DeactivateUser_and_ReactivateUser_handlers()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<DeactivateUserCommandHandler>(scope.ServiceProvider
            .GetRequiredService<ICommandHandler<DeactivateUserCommand, DeactivateUserResult>>());

        Assert.IsType<ReactivateUserCommandHandler>(scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReactivateUserCommand, ReactivateUserResult>>());
    }

    /// <summary>AUT-C2, resolved for CRD-C5's reason.</summary>
    [Fact]
    public void The_dispatcher_can_resolve_the_RevokeRole_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RevokeRoleCommand, RevokeRoleResult>>();

        Assert.IsType<RevokeRoleCommandHandler>(handler);
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

        // Rate limiting (behaviour 11) first, then three authorisation-side
        // behaviours, then notification's post-commit scope, the audit
        // command scope inside it, the transaction scope inside that, and —
        // within the transaction — notification emission and audit emission.
        Assert.Equal(9, behaviors.Count);

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
        services.AddScoped<IRoleRepository, StubRoleRepository>();
        services.AddScoped<IUserRoleRepository, StubUserRoleRepository>();
        services.AddScoped<IAuditEventCatalogue, StubAuditEventCatalogue>();
        services.AddScoped<IAuditRecordWriter, StubAuditRecordWriter>();
        services.AddScoped<IAutonomousAuditRecordWriter, StubAutonomousWriter>();
        services.AddScoped<INotificationRepository, StubNotificationRepository>();

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

    private sealed class StubNotificationRepository : INotificationRepository
    {
        public Task AddAsync(
            Notification notification,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
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
        public Task<User?> FindForUpdateAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
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
        public Task<IReadOnlyList<UserIdentity>> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsWithUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task AddAsync(
            Domain.Users.UserIdentity identity, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Domain.Users.UserIdentity?> FindLocalByUsernameAsync(
            string username, CancellationToken cancellationToken)
            => Task.FromResult<Domain.Users.UserIdentity?>(null);

        public Task<IReadOnlyList<Domain.Users.UserIdentityId>> FindPasswordResetCandidatesAsync(
            string emailOrUsername, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Domain.Users.UserIdentityId>>([]);

        public Task<IReadOnlyList<Domain.Users.UserIdentity>> FindLocalByUserIdAsync(
            Domain.Users.UserId userId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Domain.Users.UserIdentity>>([]);

        public Task<Domain.Users.UserIdentity?> FindAsync(
            Domain.Users.UserIdentityId userIdentityId,
            CancellationToken cancellationToken)
            => Task.FromResult<Domain.Users.UserIdentity?>(null);
    }

    private sealed class StubUserTokenRepository : IUserTokenRepository
    {
        public Task<int> InvalidateOutstandingForUserAsync(UserId userId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddAsync(
            Domain.Users.UserToken token, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<Domain.Users.UserTokenId>> InvalidatePriorAsync(
            Domain.Users.UserIdentityId identityId, Domain.Users.TokenType tokenType,
            DateTimeOffset now, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Domain.Users.UserTokenId>>([]);

        public Task<Domain.Users.UserIdentityId?> TryConsumeAsync(
            Domain.Users.UserTokenId tokenId, string tokenHash,
            Domain.Users.TokenType expectedType,
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

        /// <summary>
        /// Not exercised here: these tests are about the command pipeline, and
        /// the enumeration is the other view over the same evaluation (B6-B).
        /// </summary>
        public Task<IReadOnlyList<EffectivePermission>> EnumerateAsync(
            EffectivePermissionsRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<EffectivePermission>>([]);

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

        public Task<IReadOnlyList<Domain.Users.PasswordHistory>> FindRecentAsync(
            Domain.Users.UserIdentityId userIdentityId, int depth,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Domain.Users.PasswordHistory>>([]);
    }

    private sealed class StubRoleRepository : IRoleRepository
    {
        public Task<Role?> FindAsync(RoleId roleId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubUserRoleRepository : IUserRoleRepository
    {
        public Task<IReadOnlyList<UserRole>> FindForUserAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddAsync(UserRole assignment, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<UserRole?> FindAsync(UserRoleId assignmentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
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

        public Task<Domain.Users.UserSession?> FindActiveAsync(
            Domain.Users.UserSessionId sessionId,
            DateTimeOffset now,
            TimeSpan idleTimeout,
            CancellationToken cancellationToken)
            => Task.FromResult<Domain.Users.UserSession?>(null);

        public Task<IReadOnlyList<Domain.Users.UserSession>> FindActiveForUserAsync(
            Domain.Users.UserId userId,
            DateTimeOffset now,
            TimeSpan idleTimeout,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Domain.Users.UserSession>>([]);

        public Task<IReadOnlyList<Domain.Users.UserSession>> FindOtherActiveForIdentityAsync(
            Domain.Users.UserIdentityId identityId,
            Domain.Users.UserSessionId excluding,
            DateTimeOffset now,
            TimeSpan idleTimeout,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Domain.Users.UserSession>>([]);

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
