using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Persistence.Audit;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Notifications;
using Ligature.Platform.Persistence.Repositories;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ligature.Platform.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPlatformPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddScoped<IClock, SystemClock>();

        // Stateless, and RandomNumberGenerator is thread-safe.
        services.AddSingleton<IUserTokenService, UserTokenService>();

        // Resolved explicitly rather than by convention: IExecutionContext is
        // absent during provisioning, and GetService returning null is the
        // signal the interceptor reads. A GetRequiredService here would make
        // provisioning impossible to run.
        services.AddScoped(sp => new ProvenanceStampingInterceptor(
            sp.GetRequiredService<IClock>(),
            sp.GetService<IExecutionContext>()));

        services.AddDbContext<LigatureDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);

            options.AddInterceptors(
                sp.GetRequiredService<ProvenanceStampingInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IUserRepository, UserRepository>();

        services.AddScoped<IUserIdentityRepository, UserIdentityRepository>();

        services.AddScoped<IUserTokenRepository, UserTokenRepository>();

        services.AddScoped<INotificationRepository, NotificationRepository>();

        // The notification sender's database collaborators. Each holds the
        // connection string and opens its own connection (IMPL-N02), so they
        // are singletons and carry no ambient transaction — which is what lets
        // the pump run them from a background thread with no DI scope.
        //
        // All three are registered whether or not mail is configured. The
        // sweeper in particular must run either way: a Pending row on a host
        // with no mail still needs a terminus.
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<INotificationGate>(
            _ => new NotificationGate(connectionString));

        services.AddSingleton<INotificationTerminalWriter>(
            sp => new NotificationTerminalWriter(
                connectionString, sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton<INotificationSweeper>(
            _ => new NotificationSweeper(connectionString));

        services.AddScoped<ISecurityPolicyResolver, SecurityPolicyResolver>();

        services.AddScoped<IAuthorizationService, AuthorizationService>();

        // CRD-C1
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        services.AddScoped<ICredentialRepository, CredentialRepository>();

        services.AddScoped<IPasswordHistoryRepository, PasswordHistoryRepository>();

        // SES-C1
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();

        // The audit writer: scoped, on the same DbContext as the command, so
        // it writes on the transaction behaviour 6 opened. Registered against
        // an internal interface — handlers have no public seam to it (IMPL-02).
        services.AddScoped<IAuditRecordWriter, AuditRecordWriter>();

        // The autonomous writer holds no scoped state — it opens and closes
        // its own connection per call — so a singleton is honest about what
        // it is. It deliberately does NOT take the DbContext: sharing that
        // connection is exactly what it exists not to do.
        services.AddSingleton<IAutonomousAuditRecordWriter>(
            _ => new AutonomousAuditRecordWriter(connectionString));

        // The catalogue: one per process, loaded on first access. The host
        // forces that access at start and verifies its declarations against
        // it; everything else simply reads (IMPL-08).
        services.AddSingleton<IAuditEventCatalogue>(
            _ => new LazyAuditEventCatalogue(connectionString));

        // AUD-D28's establishment path: the caller of a token-bearer command,
        // established from the identity their token's consumption returned.
        // Scoped, like the context it writes into.
        services.AddScoped<IBearerActorEstablisher, BearerActorEstablisher>();

        // The User Management side of the section 17 ownership boundary. The
        // Host extracts a SessionId and asks this; it does not decide session
        // validity itself.
        services.AddScoped<ICallerEstablisher, CallerEstablisher>();

        return services;
    }
    /// <summary>
    /// Registers the mail transport. Called by the composition root ONLY when
    /// the mail settings are present; not calling it leaves INotificationTransport
    /// unregistered, which — together with the absent sender — is what puts the
    /// pump into sweep-only mode.
    ///
    /// One HttpClient for the life of the host, with a pooled-connection
    /// lifetime so a long-lived client still notices DNS changes. The transport
    /// timeout is applied here because IMPL-N03 makes it host configuration
    /// read at start, and because the sender must never be able to block a
    /// consumer indefinitely on a provider that has stopped answering.
    /// </summary>
    public static IServiceCollection AddNotificationTransport(
        this IServiceCollection services,
        MailSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<INotificationTransport>(sp =>
        {
            var http = new HttpClient(
                new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                })
            {
                Timeout = settings.TransportTimeout,
            };

            return new GmailTransport(
                http,
                new GoogleServiceAccountTokenSource(
                    http, settings, sp.GetRequiredService<TimeProvider>()),
                settings);
        });

        return services;
    }

}
