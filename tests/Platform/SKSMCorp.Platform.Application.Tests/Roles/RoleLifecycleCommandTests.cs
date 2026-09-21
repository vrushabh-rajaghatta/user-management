using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Roles.Commands.DeactivateRole;
using SKSMCorp.Platform.Application.Roles.Commands.ReactivateRole;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Tests.Roles;

/// <summary>
/// AUT-C5/C6's declarations (docs/requirements.md, "AUT-C5 DeactivateRole and
/// AUT-C6 ReactivateRole", RD5 and RD6).
///
/// THE ASYMMETRY IS THE CONTRACT. AUT-C5 carries a reason because the
/// catalogue gives it one and RoleDeactivated is seeded ReasonRequired;
/// AUT-C6 carries none, and one is not added for symmetry's sake.
/// </summary>
public sealed class RoleLifecycleCommandTests
{
    private static readonly string[] StoredRole =
        ["Code", "Description", "IsActive", "IsSystemRole", "Name", "RoleId"];

    [Fact]
    public void Both_require_role_manage()
    {
        Assert.Equal("role.manage", new DeactivateRoleCommand(RoleId.New(), "Retired.").RequiredPermission);
        Assert.Equal("role.manage", new ReactivateRoleCommand(RoleId.New()).RequiredPermission);
    }

    [Fact]
    public void Both_are_human_actor_only_commands()
    {
        Assert.True(typeof(IHumanActorOnlyCommand<DeactivateRoleResult>)
            .IsAssignableFrom(typeof(DeactivateRoleCommand)));

        Assert.True(typeof(IHumanActorOnlyCommand<ReactivateRoleResult>)
            .IsAssignableFrom(typeof(ReactivateRoleCommand)));
    }

    [Fact]
    public void Deactivation_carries_a_reason()
        => Assert.Contains(
            typeof(DeactivateRoleCommand).GetProperties(),
            x => x.Name == "Reason" && x.PropertyType == typeof(string));

    /// <summary>RD5: no reason on AUT-C6, and no way for a caller to supply one.</summary>
    [Fact]
    public void Reactivation_carries_no_reason()
        => Assert.DoesNotContain(
            typeof(ReactivateRoleCommand).GetProperties(),
            x => x.Name.Contains("Reason", StringComparison.OrdinalIgnoreCase));

    /// <summary>Neither command may move ownership or the code, and neither takes IsActive as an input.</summary>
    [Theory]
    [InlineData(typeof(DeactivateRoleCommand))]
    [InlineData(typeof(ReactivateRoleCommand))]
    public void Neither_takes_ownership_the_code_or_the_flag(Type command)
        => Assert.DoesNotContain(
            command.GetProperties(),
            x => x.Name.Contains("System", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Code", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Active", StringComparison.OrdinalIgnoreCase));

    /// <summary>RD6: both answer the role as stored, with the same six members AUT-C4 answers.</summary>
    [Theory]
    [InlineData(typeof(DeactivateRoleResult))]
    [InlineData(typeof(ReactivateRoleResult))]
    public void Both_results_carry_the_stored_role(Type result)
        => Assert.Equal(
            StoredRole,
            result.GetProperties().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
}
