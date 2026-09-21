using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// AUT-Q7's question: everyone who could exercise one permission, at an
/// instant. The reverse of <see cref="AuthorizationRequest"/> — it names no
/// user, because the users are the answer.
///
/// <para>
/// <b>At is the evaluation instant for every authorisation fact the model
/// dates</b> (RW3): the assignment's effective period, its revocation, and the
/// role-permission grant's own lifecycle. It is NOT applied to the permission
/// catalogue's IsActive or to user and identity status, neither of which the
/// schema represents historically — those remain current-state gates, and the
/// contract says so rather than letting today's state pass as history.
/// </para>
/// </summary>
/// <param name="ScopeType">
/// Kept because the frozen catalogue names it (RW7). V1 is Global-only, and a
/// Global assignment does not yet imply narrower scopes.
/// </param>
public sealed record WhoCanDoRequest(
    string PermissionCode,
    DateTimeOffset At,
    string ScopeType,
    Guid? ScopeId);

/// <summary>
/// One person who could exercise the permission, and WHY — the authorising
/// roles, which is most of the answer to an inspection question.
///
/// ONE ROW PER USER (RW5), deliberately the opposite of AUT-Q4's choice: there
/// the assignment was the identity because the dates and the reason belonged
/// to it; here the user is, and a holder who reaches the permission through
/// three roles is one row naming three.
/// </summary>
/// <param name="Status">
/// CURRENT status, never reconstructed (RW4). app_user.status is tied to
/// deactivated_at by a CHECK that nulls the timestamp on reactivation, so the
/// schema retains no status history to reconstruct from.
/// </param>
public sealed record PermissionHolder(
    UserId UserId,
    string DisplayName,
    string? Email,
    UserStatus Status,
    IReadOnlyList<HolderRole> Roles);

/// <summary>
/// An authorising role, with its name AS STORED NOW. The historical name lives
/// on the audit record's ActorSnapshot, which exists precisely so that renaming
/// a role (AUT-C4) does not rewrite the authority recorded against acts already
/// performed.
/// </summary>
public sealed record HolderRole(RoleId RoleId, string Name);
