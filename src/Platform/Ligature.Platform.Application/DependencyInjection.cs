using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Dispatching;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Commands.SignIn;
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

        AddCommandHandlers(services);

        return services;
    }

    /// <summary>
    /// Registered one by one rather than by assembly scanning, deliberately.
    /// "Which commands are wired in" is a question a reviewer should be able to
    /// answer by reading this method, not by reasoning about what a reflection
    /// predicate would have matched at startup. The list grows with the
    /// catalogue; that cost is worth paying in a validated system.
    /// </summary>
    private static void AddCommandHandlers(IServiceCollection services)
    {
        // USR-C1
        services.AddScoped<
            ICommandHandler<CreateUserCommand, CreateUserResult>,
            CreateUserCommandHandler>();

        // CRD-C1
        services.AddScoped<
            ICommandHandler<ActivateAccountCommand, ActivateAccountResult>,
            ActivateAccountCommandHandler>();

        // SES-C1
        services.AddScoped<
            ICommandHandler<SignInCommand, SignInResult>,
            SignInCommandHandler>();
    }
}