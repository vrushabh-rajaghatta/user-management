using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.RevokeRole;

/// <summary>
/// AUT-C2 — RevokeRole. RED STUB: the contract is in docs/requirements.md,
/// and this type exists only so the tests that describe it compile.
/// </summary>
public sealed record RevokeRoleCommand(
    UserRoleId AssignmentId,
    string Reason)
    : IAuthorizableCommand<RevokeRoleResult>
{
    public string RequiredPermission
        => throw new NotImplementedException("AUT-C2 is not implemented.");
}
