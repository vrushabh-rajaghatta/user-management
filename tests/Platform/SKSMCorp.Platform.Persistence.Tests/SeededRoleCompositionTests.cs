using SKSMCorp.Platform.Persistence.Provisioning;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// PRV-C1 Amendment 1, S1 (docs/requirements.md): what each seeded role
/// confers, pinned exactly.
///
/// Nothing pinned this before. CatalogueDriftTests compares the database with
/// the seed and CatalogueSynchronisationTests converges one onto the other,
/// so both follow the seed wherever it goes: a deleted or added grant line
/// passes them all. Role composition is an authorization decision, so it is
/// stated here as data a reviewer can read, and a change to it fails a test
/// by name.
/// </summary>
public sealed class SeededRoleCompositionTests
{
    [Fact]
    public void User_administrator_manages_the_account_lifecycle_and_holds_no_role_permission()
        => Assert.Equal(
            [
                "identity.manage",
                "identity.read",
                "session.read",
                "session.revoke",
                "user.create",
                "user.deactivate",
                "user.reactivate",
                "user.read",
                "user.resetpassword",
                "user.unlock",
                "user.update",
            ],
            PermissionsOf("user-administrator"));

    /// <summary>
    /// The amendment: user.read, and no other user, identity or session
    /// permission. Visibility, not user-management capability.
    /// </summary>
    [Fact]
    public void Security_administrator_manages_roles_and_policy_and_can_see_users()
        => Assert.Equal(
            [
                "role.grant",
                "role.manage",
                "role.read",
                "role.revoke",
                "securitypolicy.change",
                "securitypolicy.read",
                "user.read",
            ],
            PermissionsOf("security-administrator"));

    [Fact]
    public void Access_reviewer_reads_and_changes_nothing()
        => Assert.Equal(
            [
                "accessreview.read",
                "identity.read",
                "role.read",
                "securitypolicy.read",
                "session.read",
                "user.read",
            ],
            PermissionsOf("access-reviewer"));

    /// <summary>
    /// A composition change, not a catalogue change: the permission and role
    /// lists are untouched, and the grant list is the three above and nothing
    /// else.
    /// </summary>
    [Fact]
    public void The_catalogue_is_nineteen_permissions_three_roles_and_twenty_four_grants()
    {
        Assert.Equal(19, PlatformProvisioner.GetPermissionSeeds().Count);
        Assert.Equal(3, PlatformProvisioner.GetRoleSeeds().Count);
        Assert.Equal(24, PlatformProvisioner.GetRolePermissionSeeds().Count);
    }

    private static IReadOnlyList<string> PermissionsOf(string roleCode)
        => [.. PlatformProvisioner.GetRolePermissionSeeds()
            .Where(x => x.RoleCode == roleCode)
            .Select(x => x.PermissionCode)
            .OrderBy(x => x, StringComparer.Ordinal)];
}
