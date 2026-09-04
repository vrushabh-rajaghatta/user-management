using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Dispatching;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPlatformApplication(
        this IServiceCollection services)
    {
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