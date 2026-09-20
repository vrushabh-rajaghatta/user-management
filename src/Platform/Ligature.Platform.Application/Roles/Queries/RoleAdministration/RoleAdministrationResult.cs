namespace Ligature.Platform.Application.Roles.Queries.RoleAdministration;

/// <summary>AUT-Q5's row: the stored role, plus the three derived values (RA2-RA4).</summary>
public sealed record RoleSummary(
    Guid RoleId,
    string Code,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsActive,
    bool AgentAssignable,
    int PermissionCount,
    int ActiveHolderCount);

public sealed record RoleAdministrationResult(IReadOnlyList<RoleSummary> Roles);
