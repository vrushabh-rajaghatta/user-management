namespace Ligature.Platform.Application.Roles.Queries.RolePermissions;

/// <summary>AUT-Q3's row: the permission, and the grant's own lifecycle.</summary>
public sealed record RolePermissionGrant(
    Guid RolePermissionId,
    Guid PermissionId,
    string Code,
    string Name,
    string Resource,
    string Action,
    bool RequiresHumanActor,
    DateTimeOffset GrantedAt,
    DateTimeOffset? RevokedAt);

/// <summary>
/// RA11: an unknown role is not an empty list. Permissions is null exactly when
/// the role does not exist, which the route answers as 404; an existing role
/// with no live grants answers an empty list.
/// </summary>
public sealed record RolePermissionsResult(IReadOnlyList<RolePermissionGrant>? Permissions)
{
    public bool RoleExists => Permissions is not null;
}
