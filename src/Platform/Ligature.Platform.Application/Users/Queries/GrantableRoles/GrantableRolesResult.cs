using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Queries.GrantableRoles;

/// <summary>RED STUB.</summary>
public sealed record GrantableRolesResult(IReadOnlyList<GrantableRole> Roles);

/// <summary>RED STUB.</summary>
public sealed record GrantableRole(RoleId RoleId, string Name, string? Description);
