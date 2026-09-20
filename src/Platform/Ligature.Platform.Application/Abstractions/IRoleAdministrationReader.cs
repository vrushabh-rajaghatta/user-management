using Ligature.Platform.Application.Roles.Queries.PermissionCatalogue;
using Ligature.Platform.Application.Roles.Queries.RoleAdministration;
using Ligature.Platform.Application.Roles.Queries.RolePermissions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>AUT-Q5's read: the roles, with the three derived values (RA2-RA4).</summary>
public interface IRoleAdministrationReader
{
    Task<IReadOnlyList<RoleSummary>> ReadAsync(
        bool includeInactive,
        bool agentAssignableOnly,
        DateTimeOffset at,
        CancellationToken cancellationToken);
}

/// <summary>AUT-Q3's read: one role's grants, live at an instant.</summary>
public interface IRolePermissionReader
{
    /// <summary>Null when the role does not exist; empty when it has no live grants (RA11).</summary>
    Task<IReadOnlyList<RolePermissionGrant>?> ReadAsync(
        RoleId roleId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>AUT-Q6's read: the release-owned catalogue (PE2), filtered.</summary>
public interface IPermissionCatalogueReader
{
    Task<IReadOnlyList<PermissionCatalogueEntry>> ReadAsync(
        string? resource,
        bool? requiresHumanActor,
        CancellationToken cancellationToken);
}
