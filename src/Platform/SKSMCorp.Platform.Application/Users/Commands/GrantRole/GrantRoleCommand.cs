using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Commands.GrantRole;

/// <summary>
/// AUT-C1. An administrator grants a human user a role, Global in v1, from now
/// or a future instant, optionally until an end (docs/requirements.md, "Role
/// Assignment").
///
/// Human-only: an actor able to grant roles could grant them to itself, so the
/// loop is closed for agents in the permission catalogue (spec §6.9).
/// </summary>
/// <param name="EffectiveFrom">Optional; the server's now when omitted. Never in the past.</param>
/// <param name="EffectiveTo">Optional; open-ended when omitted. Strictly after EffectiveFrom.</param>
/// <param name="Reason">
/// Required: the access-review evidence (AssignmentReason) and the audit
/// record's reason. An external ticket reference belongs here (open decision
/// A1); its format is not validated.
/// </param>
public sealed record GrantRoleCommand(
    UserId UserId,
    RoleId RoleId,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string Reason)
    : IAuthorizableCommand<GrantRoleResult>,
      IHumanActorOnlyCommand<GrantRoleResult>
{
    public string RequiredPermission => "role.grant";
}
