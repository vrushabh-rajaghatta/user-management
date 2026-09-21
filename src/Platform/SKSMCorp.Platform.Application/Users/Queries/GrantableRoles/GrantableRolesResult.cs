using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Queries.GrantableRoles;

/// <summary>The active roles, ordered by name under ICU "unicode", then id.</summary>
public sealed record GrantableRolesResult(IReadOnlyList<GrantableRole> Roles);

/// <summary>Exactly what the grant form needs to offer a role, and no more.</summary>
public sealed record GrantableRole(RoleId RoleId, string Name, string? Description);
