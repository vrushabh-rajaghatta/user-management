using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.GrantRole;

/// <summary>
/// The new assignment's identifier: what AUT-C2 revokes it by, and what the
/// later read addresses it by.
/// </summary>
public sealed record GrantRoleResult(UserRoleId UserRoleAssignmentId);
