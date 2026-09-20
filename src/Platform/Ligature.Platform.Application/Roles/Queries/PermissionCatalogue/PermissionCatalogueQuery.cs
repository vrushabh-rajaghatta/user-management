using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Queries.PermissionCatalogue;

/// <summary>
/// AUT-Q6 ListPermissions (docs/requirements.md, "Role administration read"),
/// under role.read. The catalogue is release-owned (PE2); this only reads it.
/// </summary>
public sealed record PermissionCatalogueQuery(string? Resource, bool? RequiresHumanActor)
    : IQuery<PermissionCatalogueResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("role.read");
}
