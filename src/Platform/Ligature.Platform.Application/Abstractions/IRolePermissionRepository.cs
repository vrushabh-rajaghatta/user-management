using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// The grants a role carries (AUT-C7, AUT-C8). Narrow on purpose: the read
/// models answer questions about grants, this answers the two the commands ask.
/// </summary>
public interface IRolePermissionRepository
{
    /// <summary>
    /// The LIVE grant for this pair, if there is one (RP2). Live means not
    /// revoked, the same predicate as the partial unique index.
    /// </summary>
    Task<RolePermission?> FindLiveAsync(
        RoleId roleId,
        PermissionId permissionId,
        CancellationToken cancellationToken);

    /// <summary>One grant by id, TRACKED, because AUT-C8 is about to close it.</summary>
    Task<RolePermission?> FindTrackedAsync(
        RolePermissionId rolePermissionId,
        CancellationToken cancellationToken);

    /// <summary>AUT-C7 adds a NEW row, never an update of a revoked one (RP1).</summary>
    Task AddAsync(
        RolePermission grant,
        CancellationToken cancellationToken);
}
