using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Roles.Commands.DeactivateRole;

/// <summary>The role AS STORED (RD6), whether or not this call changed it.</summary>
public sealed record DeactivateRoleResult(
    RoleId RoleId,
    string Code,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsActive);
