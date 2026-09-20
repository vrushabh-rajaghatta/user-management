using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Tests;

/// <summary>
/// AUT-C4's rules in the domain (docs/requirements.md, "AUT-C4
/// UpdateRoleMetadata", RM3 and RM4; RM-A4 and RM-A5).
///
/// THE RULES ARE AUT-C3's, UNCHANGED. What is new is that UpdateMetadata is
/// CHANGE-AWARE (RM3): it answers whether the normalised values differ from
/// the stored ones, so the handler can write nothing and record nothing when
/// they do not. That is the USR-C2 family again — User.UpdateProfile has
/// answered the same question since USR-C2 — and not a third dialect.
///
/// A SYSTEM ROLE IS REFUSED BEFORE ANY OF IT (RM4), identical values included:
/// ownership is the coarser gate, and a silent success would say the edit had
/// been permitted.
/// </summary>
public sealed class RoleMetadataRulesTests
{
    private const string SystemRole = "System roles cannot be modified.";

    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserId Administrator = new(Guid.Parse("a0000000-0000-4000-8000-000000000001"));

    // ------------------------------------------------------------ RM3: no-op

    [Fact]
    public void Resubmitting_the_stored_values_changes_nothing()
    {
        var role = Tenant("Quality Reviewer", "Reviews access.");

        Assert.False(role.UpdateMetadata("Quality Reviewer", "Reviews access."));

        Assert.Equal("Quality Reviewer", role.Name);
        Assert.Equal("Reviews access.", role.Description);
    }

    /// <summary>The comparison is of NORMALISED values, so padding is not a change.</summary>
    [Fact]
    public void Values_that_normalise_to_the_stored_ones_change_nothing()
    {
        var role = Tenant("Quality Reviewer", "Reviews access.");

        Assert.False(role.UpdateMetadata("  Quality Reviewer  ", " Reviews access. "));
    }

    /// <summary>A description that trims away equals one that is already absent.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_description_over_an_absent_one_changes_nothing(string? description)
    {
        var role = Tenant("Quality Reviewer", null);

        Assert.False(role.UpdateMetadata("Quality Reviewer", description));
        Assert.Null(role.Description);
    }

    [Fact]
    public void A_different_name_is_a_change_and_is_stored_trimmed()
    {
        var role = Tenant("Quality Reviewer", "Reviews access.");

        Assert.True(role.UpdateMetadata("  Access Reviewer  ", "Reviews access."));

        Assert.Equal("Access Reviewer", role.Name);
        Assert.Equal("Reviews access.", role.Description);
    }

    [Fact]
    public void A_different_description_is_a_change()
    {
        var role = Tenant("Quality Reviewer", "Reviews access.");

        Assert.True(role.UpdateMetadata("Quality Reviewer", "Reviews everything."));
        Assert.Equal("Reviews everything.", role.Description);
    }

    [Fact]
    public void Clearing_a_description_is_a_change_and_stores_none()
    {
        var role = Tenant("Quality Reviewer", "Reviews access.");

        Assert.True(role.UpdateMetadata("Quality Reviewer", "   "));
        Assert.Null(role.Description);
    }

    /// <summary>Case is a change: the name is prose, not an identifier.</summary>
    [Fact]
    public void A_case_only_difference_in_the_name_is_a_change()
    {
        var role = Tenant("Quality Reviewer", null);

        Assert.True(role.UpdateMetadata("QUALITY REVIEWER", null));
        Assert.Equal("QUALITY REVIEWER", role.Name);
    }

    // ----------------------------------------------------- RM4: system roles

    [Fact]
    public void A_system_role_is_refused()
    {
        var role = Seeded("Access Reviewer", "Reviews access.");

        Assert.Equal(SystemRole, Refusal(() => role.UpdateMetadata("Renamed", "Rewritten")));

        Assert.Equal("Access Reviewer", role.Name);
        Assert.Equal("Reviews access.", role.Description);
    }

    /// <summary>RM-A5: the refusal comes FIRST, so identical values do not slip through as a no-op.</summary>
    [Fact]
    public void A_system_role_is_refused_even_when_nothing_would_change()
        => Assert.Equal(
            SystemRole,
            Refusal(() => Seeded("Access Reviewer", "Reviews access.")
                .UpdateMetadata("Access Reviewer", "Reviews access.")));

    /// <summary>Ownership is refused before the input rules: a blank name on a seed still reads as ownership.</summary>
    [Fact]
    public void A_system_role_is_refused_before_the_input_rules()
        => Assert.Equal(
            SystemRole,
            Refusal(() => Seeded("Access Reviewer", null).UpdateMetadata("", null)));

    // ----------------------------------------- RM-A4: the AUT-C3 rules again

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused_and_the_role_keeps_its_own(string name)
    {
        var role = Tenant("Quality Reviewer", null);

        Assert.Equal("A role name is required.", Refusal(() => role.UpdateMetadata(name, null)));
        Assert.Equal("Quality Reviewer", role.Name);
    }

    [Fact]
    public void A_name_with_a_control_character_is_refused()
        => Assert.Equal(
            "A role name must not contain control characters.",
            Refusal(() => Tenant("Quality Reviewer", null).UpdateMetadata("Quality\u0007Reviewer", null)));

    [Fact]
    public void A_name_of_101_code_points_is_refused_and_100_is_accepted()
    {
        Assert.Equal(
            "A role name must be at most 100 characters.",
            Refusal(() => Tenant("Quality Reviewer", null).UpdateMetadata(new string('x', 101), null)));

        var role = Tenant("Quality Reviewer", null);

        Assert.True(role.UpdateMetadata(new string('x', 100), null));
        Assert.Equal(100, role.Name.Length);
    }

    [Fact]
    public void A_description_with_a_control_character_is_refused()
        => Assert.Equal(
            "A role description must not contain control characters.",
            Refusal(() => Tenant("Quality Reviewer", null).UpdateMetadata("Quality Reviewer", "Reviews\u0007access.")));

    [Fact]
    public void A_description_of_101_code_points_is_refused()
        => Assert.Equal(
            "A role description must be at most 100 characters.",
            Refusal(() => Tenant("Quality Reviewer", null).UpdateMetadata("Quality Reviewer", new string('x', 101))));

    /// <summary>A refused edit leaves BOTH values as they were — no half-applied change.</summary>
    [Fact]
    public void A_refused_edit_changes_neither_value()
    {
        var role = Tenant("Quality Reviewer", "Reviews access.");

        Refusal(() => role.UpdateMetadata("Access Reviewer", new string('x', 101)));

        Assert.Equal("Quality Reviewer", role.Name);
        Assert.Equal("Reviews access.", role.Description);
    }

    // -------------------------------------------- RM2/RM10: what cannot move

    /// <summary>The code and the ownership flag are not this method's to touch, and it has no input for either.</summary>
    [Fact]
    public void The_code_and_the_ownership_flag_survive_an_edit()
    {
        var role = Role.Create(
            RoleId.New(), "Quality Reviewer", "quality-reviewer", null, isSystemRole: false, Now, Administrator);

        Assert.True(role.UpdateMetadata("Access Reviewer", "Reviews access."));

        Assert.Equal("quality-reviewer", role.Code);
        Assert.False(role.IsSystemRole);
        Assert.True(role.IsActive);
    }

    // ------------------------------------------------------------- harness

    private static string Refusal(Func<bool> action)
        => Assert.Throws<DomainException>(() => action()).Message;

    private static Role Tenant(string name, string? description)
        => Role.Create(RoleId.New(), name, "quality-reviewer", description, isSystemRole: false, Now, Administrator);

    private static Role Seeded(string name, string? description)
        => Role.Create(RoleId.New(), name, "access-reviewer", description, isSystemRole: true, Now, Administrator);
}
