namespace Ligature.Platform.Application.Roles.Commands.RemovePermissionFromRole;

/// <summary>Nothing to answer with: the route is 204, as AUT-C2's is.</summary>
public sealed record RemovePermissionFromRoleResult
{
    public static readonly RemovePermissionFromRoleResult Accepted = new();
}
