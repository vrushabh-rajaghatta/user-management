using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Persistence.Audit;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Repositories;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        // The catalogue: one per process, loaded on first access. The host
        // forces that access at start and verifies its declarations against
        // it; everything else simply reads (IMPL-08).
        services.AddSingleton<IAuditEventCatalogue>(
            _ => new LazyAuditEventCatalogue(connectionString));

        // The User Management side of the section 17 ownership boundary. The
        // Host extracts a SessionId and asks this; it does not decide session
        // validity itself.
        services.AddScoped<ICallerEstablisher, CallerEstablisher>();

        return services;
    }
}
