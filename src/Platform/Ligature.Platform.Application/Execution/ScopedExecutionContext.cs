using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Execution;

/// <summary>
/// The write seam for <see cref="IExecutionContext"/>, deliberately separate
/// from it. A consumer injecting IExecutionContext receives only the three
/// read members and therefore cannot rewrite the calling identity; establishing
/// it requires asking for this interface on purpose.
/// </summary>
public interface IExecutionContextInitializer
{
    /// <summary>
    /// Establishes the authenticated caller for the current scope, once.
    /// Both values are supplied together so the context can never be observed
    /// half-built — authenticated with no actor type, or with an id but not yet
    /// marked authenticated.
    /// </summary>
    void Establish(UserId userId, ActorType actorType);
}

/// <summary>
/// The caller identity for one DI scope — one command, one request, one
/// message, one test.
///
/// The composition edge establishes it explicitly; it does NOT flow by itself.
/// No AsyncLocal, and no dependency on any host. Whatever eventually sits at
/// that edge — HTTP middleware, a message consumer, a CLI, a test — calls
/// Establish the same way, which is what keeps the application layer free of
/// an ASP.NET-shaped abstraction it would later have to unpick.
///
/// Unset is the default, and an unset context does not invent an actor.
/// Provisioning depends on exactly that: PRV-C1 and PRV-C3 run with no
/// authenticated caller, and ProvenanceStampingInterceptor reads the absence
/// and attributes the write to the System actor.
/// </summary>
public sealed class ScopedExecutionContext
    : IExecutionContext, IExecutionContextInitializer
{
    private UserId? _userId;
    private ActorType? _actorType;

    public bool IsAuthenticated => _userId is not null;

    /// <summary>
    /// Throws when unauthenticated rather than returning a placeholder. There
    /// is no honest placeholder available: StronglyTypedId rejects Guid.Empty,
    /// and SYSTEM_UUID would attribute a human's absence to the System actor —
    /// false provenance in a system whose audit trail is the deliverable.
    /// </summary>
    public UserId UserId =>
        _userId ?? throw new InvalidOperationException(
            "No authenticated caller has been established for this scope, so "
            + "there is no UserId. Check IsAuthenticated first, or establish "
            + "the execution context at the composition edge.");

    /// <summary>
    /// Also throws when unauthenticated, and that is load-bearing rather than
    /// symmetric tidiness: default(ActorType) is Human, because Human is the
    /// first enum member. Returning a default here would make an unset context
    /// claim to be a human actor and sail straight through HumanActorBehavior —
    /// the check that stands between an automated caller and every signature,
    /// approval and role-granting permission in the catalogue.
    /// </summary>
    public ActorType ActorType =>
        _actorType ?? throw new InvalidOperationException(
            "No authenticated caller has been established for this scope, so "
            + "there is no ActorType. Check IsAuthenticated first.");

    public void Establish(UserId userId, ActorType actorType)
    {
        ArgumentNullException.ThrowIfNull(userId);

        // The System actor cannot authenticate, holds no identities and
        // performs no business actions (SA, UI8, UR7). An authenticated System
        // context is a contradiction, not a configuration.
        if (actorType == ActorType.System)
        {
            throw new DomainException(
                "The System actor cannot authenticate, so it cannot be the "
                + "established caller. Writes with no human caller are "
                + "attributed to it by the provenance interceptor instead.");
        }

        // The caller cannot change midway through a scope. A second call means
        // either a leaked scope or an impersonation attempt, and both should
        // stop rather than quietly rebind the identity that every audit record
        // written in this scope will carry.
        if (_userId is not null)
        {
            throw new InvalidOperationException(
                "An execution context has already been established for this "
                + "scope. The calling identity cannot change once work has "
                + "begun under it.");
        }

        _userId = userId;
        _actorType = actorType;
    }
}
