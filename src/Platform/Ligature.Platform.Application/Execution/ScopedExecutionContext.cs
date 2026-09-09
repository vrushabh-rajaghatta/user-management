using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Execution;

/// <summary>
/// The write seam for the caller's IDENTITY, deliberately separate from
/// <see cref="IExecutionContext"/>. A consumer injecting IExecutionContext
/// receives only the read members and therefore cannot rewrite the calling
/// identity; establishing it requires asking for this interface on purpose.
/// </summary>
public interface IExecutionContextInitializer
{
    /// <summary>
    /// Establishes the authenticated caller for the current scope, once.
    /// Every value is supplied together so the context can never be observed
    /// half-built — authenticated with no actor type, or with an id but no
    /// identity snapshot. AR24 makes the same rule a database constraint on
    /// the audit record: the snapshot group is all-present or all-absent.
    /// </summary>
    void Establish(UserId userId, ActorType actorType, ActorIdentity identity);
}

/// <summary>
/// The write seam for the caller's AUTHORITY, separate from the identity seam
/// above rather than folded into it.
///
/// Two reasons it is its own interface. It is written at a different moment,
/// by a different component — the authorisation behaviour, part-way through
/// the pipeline, long after authentication. And keeping it separate preserves
/// the property the identity seam was split out to get: the component that
/// records authority has no ability to rewrite who the caller is.
/// </summary>
public interface IAuthorityInitializer
{
    /// <summary>
    /// Records the assignment under which THIS command was authorised, for as
    /// long as the returned handle is held.
    ///
    /// The lifetime is a command, not a scope, and the distinction is not
    /// academic: a DI scope may dispatch several commands — the integration
    /// tests do exactly that — and each is authorised separately, possibly
    /// under a different role. Authority that outlived its command would be
    /// read by the next one, and an audit record naming the wrong authorising
    /// assignment is worse than one naming none.
    ///
    /// Disposing clears it. Establishing while one is already held is a
    /// nested or concurrent authorisation, which is a defect, and throws.
    /// </summary>
    IDisposable EstablishAuthority(AuthorizingAssignment authority);
}

/// <summary>
/// The caller identity for one DI scope.
///
/// A scope is NOT one command. It may dispatch several — a request handler or
/// a test frequently does — which is why identity and authority have
/// different lifetimes here: the caller is fixed for the scope, while the
/// authorising assignment belongs to whichever command is executing.
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
    : IExecutionContext, IExecutionContextInitializer, IAuthorityInitializer
{
    private UserId? _userId;
    private ActorType? _actorType;
    private ActorIdentity? _identity;
    private AuthorizingAssignment? _authority;

    /// <summary>
    /// Identifies WHICH establishment the current authority came from, so a
    /// handle can only clear its own. See <see cref="AuthorityLifetime"/>.
    /// </summary>
    private object? _authorityToken;

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

    /// <summary>
    /// Throws when unauthenticated, for UserId's reason. Note the difference
    /// from <see cref="Authority"/>, which returns null instead: an
    /// unauthenticated scope has no identity to report, whereas an
    /// authenticated one legitimately may have no authorising assignment.
    /// </summary>
    public ActorIdentity Identity =>
        _identity ?? throw new InvalidOperationException(
            "No authenticated caller has been established for this scope, so "
            + "there is no actor identity. Check IsAuthenticated first.");

    /// <summary>
    /// Null unless a command is currently executing under an authorising
    /// assignment. It stays null for acts that no role authorised — sign-in,
    /// self-service and token-bearer commands (AUD-D28) — and for every
    /// refused command, whose identity is established but whose authority
    /// never was. Null is a real answer, so this does not throw.
    /// </summary>
    public AuthorizingAssignment? Authority => _authority;

    public void Establish(
        UserId userId,
        ActorType actorType,
        ActorIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(identity);

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

        // AR11 — email belongs to human actors only, and the database refuses
        // a record carrying one for any other type. Caught here so the
        // contradiction surfaces at the composition edge that built it, rather
        // than as a constraint violation on a later audit write whose stack
        // says nothing about where the value came from.
        if (actorType != ActorType.Human && identity.Email is not null)
        {
            throw new DomainException(
                $"An actor of type '{actorType}' cannot carry an email "
                + "address: email is descriptive personal data that only human "
                + "actors have (AR11).");
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
        _identity = identity;
    }

    public IDisposable EstablishAuthority(AuthorizingAssignment authority)
    {
        ArgumentNullException.ThrowIfNull(authority);

        // Authority describes the caller, so there must be one. Recording an
        // authorising assignment against an unestablished caller would produce
        // a record asserting that nobody acted under some role.
        if (_userId is null)
        {
            throw new InvalidOperationException(
                "No authenticated caller has been established for this scope, "
                + "so there is nothing for this authority to belong to.");
        }

        // Sequential commands in one scope are ordinary and each establishes
        // its own. What is refused is establishing while another is still
        // held: that is a nested or concurrent authorisation, and silently
        // taking either answer would attribute one command's act to the
        // other's authority.
        if (_authority is not null)
        {
            throw new InvalidOperationException(
                "An authorising assignment is already in effect for this "
                + "scope. A command's authority is established once, for the "
                + "duration of that command, and cannot be nested.");
        }

        _authority = authority;
        _authorityToken = new object();

        return new AuthorityLifetime(this, _authorityToken);
    }

    /// <summary>
    /// Clears the authority when the command it belongs to finishes, however
    /// it finishes. A command that threw must not leave its authority behind
    /// for the next one to read.
    ///
    /// It clears only the establishment it came from. A handle disposed twice,
    /// or disposed late after a later command has established its own
    /// authority, would otherwise clear that newer command's authority and
    /// leave it acting with none recorded. Nothing in the pipeline disposes
    /// out of order today — the behaviour's `using` is well structured — but
    /// this type exists to prevent incorrect audit attribution, and a write
    /// seam should not depend on every future caller being well behaved.
    /// </summary>
    private sealed class AuthorityLifetime : IDisposable
    {
        private readonly ScopedExecutionContext _owner;
        private readonly object _token;

        internal AuthorityLifetime(ScopedExecutionContext owner, object token)
        {
            _owner = owner;
            _token = token;
        }

        public void Dispose()
        {
            // Not ours: either already disposed, or a later establishment has
            // replaced it. Either way there is nothing here to clear.
            if (!ReferenceEquals(_owner._authorityToken, _token))
                return;

            _owner._authority = null;
            _owner._authorityToken = null;
        }
    }
}
