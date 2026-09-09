using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Audit;

/// <summary>
/// The actor as the audit record will carry it: the ActorSnapshot contract
/// (UM v2.2) flattened to the audit_record columns, plus the origin kind the
/// database derives from it (AR6).
///
/// Built ONCE per emission from the execution context and persisted exactly
/// as built (AUD-1, AG3). Nothing downstream may enrich or correct it.
///
/// Three shapes exist in V1. An authenticated caller, from the context. The
/// System actor, for provisioning and cascades — SYSTEM_UUID with no
/// identity, no email and no role (AUD-D24). And no actor at all, for the
/// two event types the catalogue permits an Anonymous origin (EO5). The
/// System actor is never substituted for an unknown one (EO6): an
/// unauthenticated scope produces the anonymous shape, and AR5 refuses it
/// for every type that does not declare Anonymous.
/// </summary>
public sealed record ActorSnapshot(
    UserId? UserId,
    ActorType? ActorType,
    string? DisplayName,
    string? Username,
    string? Email,
    string? IdentityProvider,
    string? SubjectId,
    RoleId? AuthorizingRoleId,
    string? AuthorizingRoleName,
    string? AuthorizingScopeType,
    Guid? AuthorizingScopeId,
    UserRoleId? AuthorizingAssignmentId,
    string? PlatformAccessRef,
    DateTimeOffset? CapturedAt)
{
    public static ActorSnapshot Anonymous { get; } = new(
        null, null, null, null, null, null, null,
        null, null, null, null, null, null, null);

    /// <summary>
    /// The origin the record will carry, computed the same way the database
    /// computes AR6, so behaviour 14 can refuse an impermissible origin with
    /// a named defect rather than leaving it to the foreign key.
    /// </summary>
    public string OriginKind =>
        UserId is null ? "Anonymous"
        : ActorType == Domain.Users.ActorType.System ? "System"
        : "Authenticated";

    /// <summary>
    /// From the established caller. Identity was captured at authentication
    /// and authority by the authorisation behaviour for THIS command; both
    /// are read here and never re-fetched.
    /// </summary>
    public static ActorSnapshot FromContext(IExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.IsAuthenticated)
            return Anonymous;

        var identity = context.Identity;
        var authority = context.Authority;

        return new ActorSnapshot(
            context.UserId,
            context.ActorType,
            identity.DisplayName,
            identity.Username,
            identity.Email?.Value,
            identity.IdentityProvider.Value,
            identity.SubjectId,
            authority?.RoleId,
            authority?.RoleName,
            authority?.ScopeType.Value,
            authority?.ScopeId,
            authority?.AssignmentId,
            PlatformAccessRef: null,
            identity.CapturedAt);
    }

    /// <summary>
    /// SYSTEM_UUID acting as provisioning, cascade or platform-boundary
    /// issuer. No identity provider, subject, email or role (AR11 permits all
    /// four to be absent for the System type, and only for it).
    /// </summary>
    public static ActorSnapshot System(DateTimeOffset capturedAt)
        => new(
            User.SystemUserId,
            Domain.Users.ActorType.System,
            User.SystemDisplayName,
            Username: null,
            Email: null,
            IdentityProvider: null,
            SubjectId: null,
            AuthorizingRoleId: null,
            AuthorizingRoleName: null,
            AuthorizingScopeType: null,
            AuthorizingScopeId: null,
            AuthorizingAssignmentId: null,
            PlatformAccessRef: null,
            capturedAt);
}
