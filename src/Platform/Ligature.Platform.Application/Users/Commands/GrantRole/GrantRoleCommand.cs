using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.GrantRole;

/// <summary>
/// AUT-C1 — GrantRole. RED STUB: the contract is in docs/requirements.md,
/// and this type exists only so the tests that describe it compile.
/// </summary>
public sealed record GrantRoleCommand(
    UserId UserId,
    RoleId RoleId,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string Reason)
    : IAuthorizableCommand<GrantRoleResult>
{
    public string RequiredPermission
        => throw new NotImplementedException("AUT-C1 is not implemented.");
}
