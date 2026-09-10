using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Dispatching;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Application.Users.Commands.SignOut;
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

        // The authority seam resolves to that same instance for the same
        // reason: the authorisation behaviour writes what the handler and the
        // eventual audit emission read.
        services.AddScoped<IAuthorityInitializer>(
            sp => sp.GetRequiredService<ScopedExecutionContext>());

        services.AddScoped<CommandPipeline>();
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(AuthenticationBehavior<,>));

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(HumanActorBehavior<,>));

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(AuthorizationBehavior<,>));

        // The audit collector, one instance per scope under two interfaces —
        // the declaration side a handler injects, and the pipeline side
        // behaviours 6 and 7 use — for the same single-instance reason as the
        // execution context above.
        services.AddScoped<ScopedAuditEvents>();

        services.AddScoped<IAuditEvents>(
            sp => sp.GetRequiredService<ScopedAuditEvents>());

        services.AddScoped<IAuditEmissionScope>(
            sp => sp.GetRequiredService<ScopedAuditEvents>());

        // The notification collector, one instance per scope under two
        // interfaces — the declaration side an issuing command injects, and
        // the pipeline side the two notification behaviours use — for the same
        // single-instance reason as the execution context above.
        //
        // Deliberately its own collector rather than a shared one with audit:
        // an audit declaration that cannot be written fails the request, and a
        // notification that cannot be sent must not.
        services.AddScoped<ScopedNotificationEvents>();

        services.AddScoped<INotificationEvents>(
            sp => sp.GetRequiredService<ScopedNotificationEvents>());

        services.AddScoped<INotificationEmissionScope>(
            sp => sp.GetRequiredService<ScopedNotificationEvents>());

        // Registration order is execution order, outermost first.
        //
        // The notification post-commit behaviour is outside the audit command
        // scope, so a command whose autonomous audit write failed after commit
        // — which fails the request — hands off no notification: the send
        // follows the command result, and there was no result.
        //
        // The audit command scope is outside the transaction: it owns the
        // OperationId and the command clock, which belong to the command
        // rather than to either transaction, and it writes the autonomous
        // records once the transaction has finished, whichever way it
        // finished. Behaviour 6 then opens the transaction. Inside it, the
        // notification emission behaviour clears per attempt and adds the
        // Pending row, and behaviour 7 writes the transactional audit records
        // after the handler returns; 6 commits (IMPL-05: 1 → 2 → 5 → 3 → 4 →
        // 6 → handler → 7, with 5 satisfied before the pipeline by
        // CallerEstablisher).
        //
        // Notification emission sits OUTSIDE behaviour 7, so on the way out
        // the audit rows are written before the Pending row. Both are on the
        // command's transaction, so this is control flow rather than a
        // difference in atomicity.
        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(NotificationPostCommitBehavior<,>));

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(AuditCommandScopeBehavior<,>));

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(TransactionScopeBehavior<,>));

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(NotificationEmissionBehavior<,>));

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(AuditEmissionBehavior<,>));

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

        // SES-C2
        services.AddScoped<
            ICommandHandler<SignOutCommand, SignOutResult>,
            SignOutCommandHandler>();
    }
}