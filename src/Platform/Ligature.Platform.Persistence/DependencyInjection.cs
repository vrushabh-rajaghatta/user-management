using Ligature.Platform.Application.Abstractions;
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

        return services;
    }
}
