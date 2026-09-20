using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Roles.Commands.AddPermissionToRole;

/// <summary>The new grant's id, as AUT-C1 answers with the new assignment's.</summary>
public sealed record AddPermissionToRoleResult(RolePermissionId RolePermissionId);
