using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Roles.Commands.ReactivateRole;

/// <summary>The role AS STORED (RD6), whether or not this call changed it.</summary>
public sealed record ReactivateRoleResult(
    RoleId RoleId,
    string Code,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsActive);
