using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.GrantRole;
using Ligature.Platform.Application.Users.Commands.RevokeRole;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Users;

/// <summary>
/// AUT-C1 / AUT-C2 declarations: what the pipeline enforces before either
/// handler runs. Both are human-only, which is what closes the agent
/// self-escalation loop (spec §6.9): an actor able to grant roles could grant
/// them to itself.
/// </summary>
public sealed class RoleAssignmentCommandTests
{
    [Fact]
    public void GrantRole_requires_role_grant()
    {
        var command = new GrantRoleCommand(UserId.New(), RoleId.New(), null, null, "Onboarding.");

        Assert.Equal("role.grant", command.RequiredPermission);
    }

    [Fact]
    public void RevokeRole_requires_role_revoke()
    {
        var command = new RevokeRoleCommand(UserRoleId.New(), "Left the team.");

        Assert.Equal("role.revoke", command.RequiredPermission);
    }

    [Fact]
    public void GrantRole_is_human_actor_only()
    {
        Assert.True(
            typeof(IHumanActorOnlyCommand<GrantRoleResult>).IsAssignableFrom(typeof(GrantRoleCommand)));
    }

    [Fact]
    public void RevokeRole_is_human_actor_only()
    {
        Assert.True(
            typeof(IHumanActorOnlyCommand<RevokeRoleResult>).IsAssignableFrom(typeof(RevokeRoleCommand)));
    }
}
