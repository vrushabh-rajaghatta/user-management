using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class RolePermission : Entity<RolePermissionId>
{
    private RolePermission(
        RolePermissionId id,
        RoleId roleId,
        PermissionId permissionId,
        DateTimeOffset grantedAt,
        UserId grantedBy,
        DateTimeOffset? revokedAt,
        UserId? revokedBy)
        : base(id)
    {
        RoleId = roleId;
        PermissionId = permissionId;
        GrantedAt = grantedAt;
        GrantedBy = grantedBy;
        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
    }

    public RoleId RoleId { get; }

    public PermissionId PermissionId { get; }

    public DateTimeOffset GrantedAt { get; }

    public UserId GrantedBy { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public UserId? RevokedBy { get; private set; }

    public bool IsActive
        => RevokedAt is null;

    public static RolePermission Create(
        RolePermissionId id,
        RoleId roleId,
        PermissionId permissionId,
        DateTimeOffset grantedAt,
        UserId grantedBy)
    {
        ArgumentNullException.ThrowIfNull(grantedBy);

        return new RolePermission(
            id,
            roleId,
            permissionId,
            grantedAt,
            grantedBy,
            revokedAt: null,
            revokedBy: null);
    }

    public void Revoke(
        DateTimeOffset revokedAt,
        UserId revokedBy)
    {
        ArgumentNullException.ThrowIfNull(revokedBy);

        if (RevokedAt is not null)
            throw new DomainException(
                "Role permission has already been revoked.");

        if (revokedAt < GrantedAt)
            throw new DomainException(
                "Permission revocation cannot occur before the grant.");

        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
    }
}