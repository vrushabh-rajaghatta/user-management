using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Queries.RoleAssignments;

/// <summary>AUT-Q2's response: one user's assignments, latest start first.</summary>
public sealed record UserRoleAssignmentsResult(IReadOnlyList<UserRoleAssignment> Assignments);

/// <summary>
/// One assignment as served: the stored facts, including the full provenance
/// (who granted and revoked it, when and why — the reason this read exists),
/// and the State derived at the instant of the read.
/// </summary>
public sealed record UserRoleAssignment(
    UserRoleId AssignmentId,
    RoleId RoleId,
    string RoleName,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    RoleAssignmentState State,
    DateTimeOffset AssignedAt,
    AssignmentActor AssignedBy,
    string AssignmentReason,
    DateTimeOffset? RevokedAt,
    AssignmentActor? RevokedBy,
    string? RevocationReason);

/// <summary>An administrator named on an assignment: who granted or revoked it.</summary>
public sealed record AssignmentActor(UserId UserId, string DisplayName);

/// <summary>An assignment as STORED: facts only. It has no state, because state is never stored.</summary>
public sealed record UserRoleAssignmentRecord(
    UserRoleId AssignmentId,
    RoleId RoleId,
    string RoleName,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset AssignedAt,
    AssignmentActor AssignedBy,
    string AssignmentReason,
    DateTimeOffset? RevokedAt,
    AssignmentActor? RevokedBy,
    string? RevocationReason);
