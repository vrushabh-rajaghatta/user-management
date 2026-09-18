using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Queries.RoleAssignments;

/// <summary>AUT-Q2. RED STUB.</summary>
public sealed record UserRoleAssignmentsResult(IReadOnlyList<UserRoleAssignment> Assignments);

/// <summary>AUT-Q2. RED STUB.</summary>
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

/// <summary>AUT-Q2. RED STUB.</summary>
public sealed record AssignmentActor(UserId UserId, string DisplayName);

/// <summary>AUT-Q2. RED STUB: an assignment as stored, before its state is derived.</summary>
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
