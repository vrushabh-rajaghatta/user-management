using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Roles.Commands.UpdateRoleMetadata;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Roles;

/// <summary>
/// AUT-C4's declarations (docs/requirements.md, "AUT-C4 UpdateRoleMetadata",
/// RM2, RM8 and RM10).
///
/// WHAT IS ABSENT IS THE CONTRACT. The command carries no Code, so there is no
/// request that could ask for one; no IsSystemRole, so ownership cannot be
/// moved; and no Reason, because the catalogue gives AUT-C4 none and
/// RoleUpdated is seeded ReasonRequired: false.
/// </summary>
public sealed class UpdateRoleMetadataCommandTests
{
    private static UpdateRoleMetadataCommand Command()
        => new(RoleId.New(), "Quality Reviewer", null);

    [Fact]
    public void It_requires_role_manage()
        => Assert.Equal("role.manage", Command().RequiredPermission);

    [Fact]
    public void It_is_a_human_actor_only_command()
        => Assert.True(
            typeof(IHumanActorOnlyCommand<UpdateRoleMetadataResult>)
                .IsAssignableFrom(typeof(UpdateRoleMetadataCommand)));

    [Fact]
    public void It_has_no_code_input()
        => Assert.DoesNotContain(
            typeof(UpdateRoleMetadataCommand).GetProperties(),
            x => x.Name.Contains("Code", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void It_has_no_IsSystemRole_input()
        => Assert.DoesNotContain(
            typeof(UpdateRoleMetadataCommand).GetProperties(),
            x => x.Name.Contains("System", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void It_has_no_reason_input()
        => Assert.DoesNotContain(
            typeof(UpdateRoleMetadataCommand).GetProperties(),
            x => x.Name.Contains("Reason", StringComparison.OrdinalIgnoreCase));

    /// <summary>RM6: the answer is the role AS STORED, so it carries what the caller never sent.</summary>
    [Fact]
    public void Its_result_carries_the_stored_role()
        => Assert.Equal(
            ["Code", "Description", "IsActive", "IsSystemRole", "Name", "RoleId"],
            typeof(UpdateRoleMetadataResult).GetProperties()
                .Select(x => x.Name)
                .OrderBy(x => x, StringComparer.Ordinal));
}
