using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Roles.Commands.UpdateRoleMetadata;

/// <summary>The role AS STORED (RM6), whether or not this call changed it.</summary>
public sealed record UpdateRoleMetadataResult(
    RoleId RoleId,
    string Code,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsActive);
