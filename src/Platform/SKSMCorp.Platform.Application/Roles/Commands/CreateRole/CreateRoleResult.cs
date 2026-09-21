using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Roles.Commands.CreateRole;

/// <summary>The created role, as stored (RC4): narrow, and no counts.</summary>
public sealed record CreateRoleResult(
    RoleId RoleId,
    string Code,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsActive);
