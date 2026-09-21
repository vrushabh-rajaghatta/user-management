using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Roles.Queries.RoleMembers;

/// <summary>
/// AUT-Q4's answer. Members is null exactly when the role does not exist,
/// which the route answers as 404 (RH8); an existing role nobody holds answers
/// an empty list and a count of zero.
/// </summary>
/// <param name="AsOf">
/// Echoed because it is the instant the whole answer was judged at, and a
/// caller that sent none cannot otherwise know which instant it received.
/// </param>
/// <param name="ActiveHolderCount">
/// DISTINCT holders among the rows being returned (RH5), keeping RA2's
/// meaning. Derived from those rows rather than counted separately, so the
/// list and the count cannot disagree.
/// </param>
public sealed record RoleMembersResult(
    DateTimeOffset AsOf,
    int ActiveHolderCount,
    IReadOnlyList<RoleMember>? Members)
{
    public bool RoleExists => Members is not null;
}

/// <summary>
/// One holding, as served. THE ASSIGNMENT IS THE IDENTITY (RH5): the dates and
/// the reason belong to the assignment, not to the person.
///
/// No RevokedAt, RevokedBy or RevocationReason: only assignments Active at
/// asOf are returned, so all three would be permanently null, and a field that
/// is always null is decoration rather than a contract. No ActorType and no
/// scope member either — agents cannot exist (invariant 17a, AU11) and RH3
/// introduces no scope semantics.
/// </summary>
/// <param name="Email">Nullable because the column is, exactly as USR-Q2's row records.</param>
/// <param name="Status">
/// Projection only (RH6). Membership is decided by the assignment; nothing
/// about the holder's status removes a row, so an anomalous holder is visible
/// rather than hidden.
/// </param>
public sealed record RoleMember(
    UserRoleId AssignmentId,
    UserId UserId,
    string DisplayName,
    string? Email,
    UserStatus Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset AssignedAt,
    RoleMemberActor AssignedBy,
    string AssignmentReason);

/// <summary>The administrator who granted a holding.</summary>
public sealed record RoleMemberActor(UserId UserId, string DisplayName);

/// <summary>
/// A holding as STORED: facts only, with no state and no count. The handler
/// derives both, once — as AUT-Q2's reader and handler already divide the work.
/// </summary>
public sealed record RoleMemberRecord(
    UserRoleId AssignmentId,
    UserId UserId,
    string DisplayName,
    string? Email,
    UserStatus Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset AssignedAt,
    RoleMemberActor AssignedBy,
    string AssignmentReason,
    DateTimeOffset? RevokedAt);
