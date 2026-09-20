namespace Ligature.Platform.Application.Roles.Queries.PermissionCatalogue;

/// <summary>AUT-Q6's row: the catalogue entry, read-only.</summary>
public sealed record PermissionCatalogueEntry(
    Guid PermissionId,
    string Code,
    string Name,
    string Resource,
    string Action,
    bool RequiresHumanActor,
    bool IsActive);

public sealed record PermissionCatalogueResult(IReadOnlyList<PermissionCatalogueEntry> Permissions);
