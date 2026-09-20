using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Roles.Commands.AddPermissionToRole;
using Ligature.Platform.Application.Roles.Commands.RemovePermissionFromRole;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Roles;

/// <summary>
/// AUT-C7/C8's declarations (docs/requirements.md, RG5 and RG8).
///
/// The asymmetry is the catalogue's: AUT-C8 carries a reason and AUT-C7 does
/// not, matching the two seeds' ReasonRequired.
/// </summary>
public sealed class RolePermissionCommandTests
{
    [Fact]
    public void Both_require_role_manage()
    {
        Assert.Equal(
            "role.manage",
            new AddPermissionToRoleCommand(RoleId.New(), PermissionId.New()).RequiredPermission);

        Assert.Equal(
            "role.manage",
            new RemovePermissionFromRoleCommand(RolePermissionId.New(), "No longer needed.").RequiredPermission);
    }

    [Fact]
    public void Both_are_human_actor_only_commands()
    {
        Assert.True(typeof(IHumanActorOnlyCommand<AddPermissionToRoleResult>)
            .IsAssignableFrom(typeof(AddPermissionToRoleCommand)));

        Assert.True(typeof(IHumanActorOnlyCommand<RemovePermissionFromRoleResult>)
            .IsAssignableFrom(typeof(RemovePermissionFromRoleCommand)));
    }

    [Fact]
    public void Adding_carries_no_reason()
        => Assert.DoesNotContain(
            typeof(AddPermissionToRoleCommand).GetProperties(),
            x => x.Name.Contains("Reason", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Removing_carries_a_reason()
        => Assert.Contains(
            typeof(RemovePermissionFromRoleCommand).GetProperties(),
            x => x.Name == "Reason" && x.PropertyType == typeof(string));

    /// <summary>Neither takes a role's ownership, a grant's revocation state, or a timestamp.</summary>
    [Theory]
    [InlineData(typeof(AddPermissionToRoleCommand))]
    [InlineData(typeof(RemovePermissionFromRoleCommand))]
    public void Neither_takes_ownership_or_lifecycle_state(Type command)
        => Assert.DoesNotContain(
            command.GetProperties(),
            x => x.Name.Contains("System", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Revoked", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Granted", StringComparison.OrdinalIgnoreCase));

    /// <summary>AUT-C7 answers the new grant's id, as AUT-C1 answers the new assignment's.</summary>
    [Fact]
    public void Adding_answers_the_new_grants_id()
        => Assert.Equal(
            ["RolePermissionId"],
            typeof(AddPermissionToRoleResult).GetProperties().Select(x => x.Name));
}
