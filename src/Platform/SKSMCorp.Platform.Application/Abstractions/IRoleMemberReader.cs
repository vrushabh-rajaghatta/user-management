using SKSMCorp.Platform.Application.Roles.Queries.RoleMembers;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// AUT-Q4's read: every holding of one role, as stored.
///
/// SHAPED FOR REV-Q1 TO REUSE (RH2). REV-Q1 GetAccessReviewReport asks the
/// same question of the same tables under accessreview.read, and the two must
/// not maintain independent assignment-state derivations. This returns stored
/// facts and derives nothing, so the state is derived once, by the handler,
/// through RoleAssignmentStates.At.
/// </summary>
public interface IRoleMemberReader
{
    /// <summary>
    /// Every assignment of the role, whatever its state, with the holder's and
    /// the granting administrator's stored values; empty for a role nobody has
    /// ever held, and null for a role that does not exist (RH8).
    /// </summary>
    Task<IReadOnlyList<RoleMemberRecord>?> ReadAsync(
        RoleId roleId,
        CancellationToken cancellationToken);
}
