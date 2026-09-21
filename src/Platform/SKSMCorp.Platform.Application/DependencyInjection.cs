using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Application.Behaviors;
using SKSMCorp.Platform.Application.RateLimiting;
using SKSMCorp.Platform.Application.Dispatching;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Application.Notifications;
using SKSMCorp.Platform.Application.Users.Commands.ActivateAccount;
using SKSMCorp.Platform.Application.Users.Commands.AdminResetPassword;
using SKSMCorp.Platform.Application.Users.Commands.ChangePassword;
using SKSMCorp.Platform.Application.Users.Commands.ResetPassword;
using SKSMCorp.Platform.Application.Users.Commands.CreateUser;
using SKSMCorp.Platform.Application.Users.Commands.GrantRole;
using SKSMCorp.Platform.Application.Users.Commands.ReissueActivationLink;
using SKSMCorp.Platform.Application.Users.Commands.DeactivateUser;
using SKSMCorp.Platform.Application.Users.Commands.ReactivateUser;
using SKSMCorp.Platform.Application.Users.Commands.RevokeRole;
using SKSMCorp.Platform.Application.Users.Commands.ChangeUserEmail;
using SKSMCorp.Platform.Application.Users.Commands.UpdateUserProfile;
using SKSMCorp.Platform.Application.Users.Queries.EffectivePermissions;
using SKSMCorp.Platform.Application.Users.Queries.UserIdentities;
using SKSMCorp.Platform.Application.Users.Queries.MySessions;
using SKSMCorp.Platform.Application.Users.Queries.UserSessions;
using SKSMCorp.Platform.Application.Roles.Commands.CreateRole;
using SKSMCorp.Platform.Application.Roles.Commands.AddPermissionToRole;
using SKSMCorp.Platform.Application.Roles.Commands.RemovePermissionFromRole;
using SKSMCorp.Platform.Application.Roles.Commands.DeactivateRole;
using SKSMCorp.Platform.Application.Roles.Commands.ReactivateRole;
using SKSMCorp.Platform.Application.Roles.Commands.UpdateRoleMetadata;
using SKSMCorp.Platform.Application.Roles.Queries.PermissionCatalogue;
using SKSMCorp.Platform.Application.Roles.Queries.RoleAdministration;
using SKSMCorp.Platform.Application.Roles.Queries.RoleMembers;
using SKSMCorp.Platform.Application.Roles.Queries.WhoCanDo;
using SKSMCorp.Platform.Application.Roles.Queries.RolePermissions;
using SKSMCorp.Platform.Application.Users.Queries.UsernameAvailability;
using SKSMCorp.Platform.Application.Users.Queries.UserProfile;
using SKSMCorp.Platform.Application.Users.Commands.RequestPasswordReset;
using SKSMCorp.Platform.Application.Users.Commands.SignIn;
using SKSMCorp.Platform.Application.Users.Commands.SignOut;
using SKSMCorp.Platform.Application.Users.Commands.RevokeSession;
using SKSMCorp.Platform.Application.Users.Commands.RevokeUserSessions;
using SKSMCorp.Platform.Application.Users.Commands.SignOutEverywhere;
using SKSMCorp.Platform.Application.Users.Commands.UnlockAccount;
using SKSMCorp.Platform.Application.Users.Queries.Me;
using SKSMCorp.Platform.Application.Users.Queries.GrantableRoles;
using SKSMCorp.Platform.Application.Users.Queries.RoleAssignments;
using SKSMCorp.Platform.Application.Users.Queries.UserList;
using SKSMCorp.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SKSMCorp.Platform.Application;

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

        // Queries are dispatched but NOT piped (docs/architecture.md section
        // 11, B6-A). There is no QueryPipeline to register beside
        // CommandPipeline, and no query behaviour below: a dispatcher is not a
        // pipeline, and nothing is copied onto queries by symmetry with
        // commands.
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();

        // Behaviour 11, FIRST: a refused request reaches nothing after it —
        // not authentication, not the transaction, not the handler, and so no
        // password derivation (docs/requirements.md, "Behaviour 11"). The
        // store is a singleton: the buckets belong to the process, not to a
        // request.
        services.AddSingleton<RateLimitStore>();

        services.AddScoped(typeof(ICommandBehavior<,>),
            typeof(RateLimitBehavior<,>));

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
        AddQueryHandlers(services);

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

        // By factory, not by type. NotificationSender and its constructor are
        // internal on purpose, and the container can only call a public
        // constructor: registered by type, it could not be built, and every host
        // with mail configured failed to start. No test built it until
        // MailDeliveryStartupTests, because every other host runs with mail
        // unconfigured, which registers no sender at all.
        services.AddSingleton(sp => new NotificationSender(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<INotificationGate>(),
            sp.GetRequiredService<NotificationTemplates>(),
            sp.GetRequiredService<INotificationTransport>(),
            sp.GetRequiredService<INotificationTerminalWriter>()));

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

        // CRD-C2
        services.AddScoped<
            ICommandHandler<RequestPasswordResetCommand, RequestPasswordResetResult>,
            RequestPasswordResetCommandHandler>();

        // CRD-C3
        services.AddScoped<
            ICommandHandler<ResetPasswordCommand, ResetPasswordResult>,
            ResetPasswordCommandHandler>();

        // SES-C3
        services.AddScoped<
            ICommandHandler<RevokeSessionCommand, RevokeSessionResult>,
            RevokeSessionCommandHandler>();

        // SES-C4 — administrator form
        services.AddScoped<
            ICommandHandler<RevokeUserSessionsCommand, RevokeUserSessionsResult>,
            RevokeUserSessionsCommandHandler>();

        // SES-C4 — self form
        services.AddScoped<
            ICommandHandler<SignOutEverywhereCommand, SignOutEverywhereResult>,
            SignOutEverywhereCommandHandler>();

        // CRD-C6
        services.AddScoped<
            ICommandHandler<UnlockAccountCommand, UnlockAccountResult>,
            UnlockAccountCommandHandler>();

        // CRD-C4
        services.AddScoped<
            ICommandHandler<ChangePasswordCommand, ChangePasswordResult>,
            ChangePasswordCommandHandler>();

        // CRD-C5
        services.AddScoped<
            ICommandHandler<AdminResetPasswordCommand, AdminResetPasswordResult>,
            AdminResetPasswordCommandHandler>();

        // CRD-C7
        services.AddScoped<
            ICommandHandler<ReissueActivationLinkCommand, ReissueActivationLinkResult>,
            ReissueActivationLinkCommandHandler>();

        // AUT-C1
        services.AddScoped<
            ICommandHandler<GrantRoleCommand, GrantRoleResult>,
            GrantRoleCommandHandler>();

        // AUT-C2
        services.AddScoped<
            ICommandHandler<RevokeRoleCommand, RevokeRoleResult>,
            RevokeRoleCommandHandler>();

        // USR-C2.
        services.AddScoped<
            ICommandHandler<UpdateUserProfileCommand, UpdateUserProfileResult>,
            UpdateUserProfileCommandHandler>();

        // USR-C3.
        services.AddScoped<
            ICommandHandler<ChangeUserEmailCommand, ChangeUserEmailResult>,
            ChangeUserEmailCommandHandler>();

        // AUT-C3.
        services.AddScoped<
            ICommandHandler<CreateRoleCommand, CreateRoleResult>,
            CreateRoleCommandHandler>();

        // AUT-C4.
        services.AddScoped<
            ICommandHandler<UpdateRoleMetadataCommand, UpdateRoleMetadataResult>,
            UpdateRoleMetadataCommandHandler>();

        // AUT-C5 / AUT-C6.
        services.AddScoped<
            ICommandHandler<DeactivateRoleCommand, DeactivateRoleResult>,
            DeactivateRoleCommandHandler>();

        services.AddScoped<
            ICommandHandler<ReactivateRoleCommand, ReactivateRoleResult>,
            ReactivateRoleCommandHandler>();

        // AUT-C7 / AUT-C8.
        services.AddScoped<
            ICommandHandler<AddPermissionToRoleCommand, AddPermissionToRoleResult>,
            AddPermissionToRoleCommandHandler>();

        services.AddScoped<
            ICommandHandler<RemovePermissionFromRoleCommand, RemovePermissionFromRoleResult>,
            RemovePermissionFromRoleCommandHandler>();

        // USR-C4 / USR-C5.
        services.AddScoped<
            ICommandHandler<DeactivateUserCommand, DeactivateUserResult>,
            DeactivateUserCommandHandler>();

        services.AddScoped<
            ICommandHandler<ReactivateUserCommand, ReactivateUserResult>,
            ReactivateUserCommandHandler>();

        // SES-C1
        services.AddScoped<
            ICommandHandler<SignInCommand, SignInResult>,
            SignInCommandHandler>();

        // SES-C2
        services.AddScoped<
            ICommandHandler<SignOutCommand, SignOutResult>,
            SignOutCommandHandler>();
    }

    /// <summary>
    /// Explicit, one line per handler, exactly as commands are registered
    /// above. Nothing scans, so a handler that exists but was never registered
    /// fails loudly on first dispatch instead of being found by magic.
    ///
    /// Through AddQuery and never a raw AddScoped (docs/architecture.md section
    /// 11). A forgotten authorization check fails SILENTLY — it serves the data
    /// — so the classification is compulsory here and verified again at
    /// start-up.
    /// </summary>
    private static void AddQueryHandlers(IServiceCollection services)
    {
        // B6
        services.AddQuery<MeQuery, MeResult, MeQueryHandler>();

        // USR-Q2
        services.AddQuery<UsersQuery, UsersResult, UsersQueryHandler>();

        // AUT-Q2
        services.AddQuery<UserRoleAssignmentsQuery, UserRoleAssignmentsResult, UserRoleAssignmentsQueryHandler>();

        // USR-Q1 GetUser, narrow v1.
        services.AddQuery<UserProfileQuery, UserProfileResult, UserProfileQueryHandler>();
        services.AddQuery<UserIdentitiesQuery, UserIdentitiesResult, UserIdentitiesQueryHandler>();
        services.AddScoped<ActiveSessionListing>();
        services.AddQuery<UserSessionsQuery, UserSessionsResult, UserSessionsQueryHandler>();
        services.AddQuery<MySessionsQuery, MySessionsResult, MySessionsQueryHandler>();
        services.AddQuery<UsernameAvailabilityQuery, UsernameAvailabilityResult, UsernameAvailabilityQueryHandler>();

        // AUT-Q5, AUT-Q3, AUT-Q6 — the role administration reads, all role.read.
        services.AddQuery<RoleAdministrationQuery, RoleAdministrationResult, RoleAdministrationQueryHandler>();
        services.AddQuery<RolePermissionsQuery, RolePermissionsResult, RolePermissionsQueryHandler>();
        services.AddQuery<PermissionCatalogueQuery, PermissionCatalogueResult, PermissionCatalogueQueryHandler>();

        // AUT-Q4 — who holds a role. The only read requiring two permissions
        // (RH9): role.read AND user.read.
        services.AddQuery<RoleMembersQuery, RoleMembersResult, RoleMembersQueryHandler>();

        // AUT-Q7 — the reverse lookup, the second read requiring two permissions.
        services.AddQuery<WhoCanDoQuery, WhoCanDoResult, WhoCanDoQueryHandler>();

        // USR-Q3 — one user's effective permission set. Third read requiring
        // two permissions, and the only one that needed a catalogue amendment
        // to get there (UA2).
        services.AddQuery<UserEffectivePermissionsQuery, UserEffectivePermissionsResult, UserEffectivePermissionsQueryHandler>();

        // The grantable-role list: a Story 2 dependency of AUT-C1, not AUT-Q5.
        services.AddQuery<GrantableRolesQuery, GrantableRolesResult, GrantableRolesQueryHandler>();
    }
}
