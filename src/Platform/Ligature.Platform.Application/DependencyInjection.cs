using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Dispatching;
using Ligature.Platform.Application.Execution;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPlatformApplication(
        this IServiceCollection services)
    {
        // One instance per scope, exposed under two interfaces: the read side
        // that behaviours and handlers consume, and the write side the
        // composition edge uses to establish the caller. Registering the
        // concrete type separately is what makes both resolve to the SAME
        // instance — two AddScoped<Interface, Impl>() calls would silently give
        // a scope two contexts, and establishing one would not be visible to
        // the other.
        services.AddScoped<ScopedExecutionContext>();

        services.AddScoped<IExecutionContext>(
            sp => sp.GetRequiredService<ScopedExecutionContext>());

        services.AddScoped<IExecutionContextInitializer>(
            sp => sp.GetRequiredService<ScopedExecutionContext>());

        services.AddScoped<CommandPipeline>();
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(AuthenticationBehavior<,>));

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(HumanActorBehavior<,>));

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(AuthorizationBehavior<,>));

        return services;
    }
}