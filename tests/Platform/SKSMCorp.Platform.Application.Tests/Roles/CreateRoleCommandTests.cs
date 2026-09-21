using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Roles.Commands.CreateRole;

namespace SKSMCorp.Platform.Application.Tests.Roles;

/// <summary>
/// AUT-C3's declarations (docs/requirements.md, "AUT-C3 CreateRole", RC4, RC7
/// and RC9). The command carries no IsSystemRole: the domain decides ownership,
/// so no caller can ask for a release-owned role.
/// </summary>
public sealed class CreateRoleCommandTests
{
    [Fact]
    public void It_requires_role_manage()
        => Assert.Equal(
            "role.manage",
            new CreateRoleCommand("quality-reviewer", "Quality Reviewer", null).RequiredPermission);

    [Fact]
    public void It_is_a_human_actor_only_command()
        => Assert.True(
            typeof(IHumanActorOnlyCommand<CreateRoleResult>).IsAssignableFrom(typeof(CreateRoleCommand)));

    [Fact]
    public void It_has_no_IsSystemRole_input()
        => Assert.DoesNotContain(
            typeof(CreateRoleCommand).GetProperties(),
            x => x.Name.Contains("System", StringComparison.OrdinalIgnoreCase));
}
