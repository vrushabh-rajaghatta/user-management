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
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // The pump is registered whether or not mail is configured: the
        // abandonment sweep is not a delivery concern, and a deployment
        // without mail must still give Pending rows a terminus.
        //
        // THE HANDOFF IS NOT REGISTERED HERE. It arrives with the sender, in
        // AddNotificationDelivery, because a queue with no consumer would hold
        // live token plaintexts in a singleton until the process exited —
        // which is precisely the retention the phase boundary exists to
        // prevent. With no sender there is no handoff, the post-commit
        // behaviour hands off nothing, and the committed Pending row is closed
        // by the sweeper as Abandoned. Honest, and nothing is retained.
        services.AddSingleton<INotificationPump, NotificationPump>();

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
    /// The half of notification delivery that only exists when mail is
    /// configured: the templates that render a message, and the sender that
    /// gates, renders, transports and closes one notification.
    ///
    /// Called by the composition root ONLY when the mail settings are present.
    /// Not calling it is what puts the pump into sweep-only mode, and that is
    /// the whole mechanism — there is no flag, no null transport and no stand-in
    /// adapter that accepts a message and drops it. A host without mail simply
    /// has no sender, which is exactly what its Abandoned rows will say.
    ///
    /// The transport itself is registered separately by the persistence module,
    /// because everything provider-specific lives behind INotificationTransport.
    /// </summary>
    public static IServiceCollection AddNotificationDelivery(
        this IServiceCollection services,
        Uri publicBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(publicBaseUrl);

        // Not IClock: that is registered scoped, and the sender is resolved by
        // a singleton pump. See NotificationSender for why the alternative —
        // changing IClock's lifetime — would reach outside this slice.
        services.TryAddSingleton(TimeProvider.System);

        // Registered together with the sender, and only with it: the handoff
        // must never outlive the consumer that gives its contents a terminal
        // disposition.
        services.AddSingleton<BoundedNotificationHandoff>();

        services.AddSingleton<INotificationHandoff>(
            sp => sp.GetRequiredService<BoundedNotificationHandoff>());

        services.AddSingleton(new NotificationTemplates(publicBaseUrl));

        services.AddSingleton<NotificationSender>();

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