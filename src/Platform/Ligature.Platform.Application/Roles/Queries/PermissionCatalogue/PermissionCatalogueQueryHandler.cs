using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Queries.PermissionCatalogue;

// Compile-only stub for the red tests of "Role administration read"
// (docs/requirements.md). Not yet implemented: it reads nothing.
public sealed class PermissionCatalogueQueryHandler
    : IQueryHandler<PermissionCatalogueQuery, PermissionCatalogueResult>
{
    public Task<PermissionCatalogueResult> Handle(PermissionCatalogueQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new PermissionCatalogueResult([]));
}
