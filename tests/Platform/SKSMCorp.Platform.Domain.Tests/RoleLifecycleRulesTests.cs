using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Domain.Tests;

/// <summary>
/// AUT-C5/C6's rules in the domain (docs/requirements.md, "AUT-C5
/// DeactivateRole and AUT-C6 ReactivateRole", RD4; RD-A3 and RD-A5).
///
/// IDEMPOTENT, NOT REFUSING (RD4). Reaching the state a role already holds is
/// a successful no-op, exactly as UpdateMetadata treats unchanged metadata.
/// The refusals "Role is already inactive." and "Role is already active."
/// are deliberately GONE: keeping them beside an idempotent command would be
/// two answers to one question.
///
/// OWNERSHIP IS REFUSED FIRST. A release-owned role is not a valid target
/// whatever state it is in, so a system role that is already inactive still
/// reads as an ownership refusal and never as a no-op.
/// </summary>
public sealed class RoleLifecycleRulesTests
{
    private const string NoDeactivate = "System roles cannot be deactivated.";

    private const string NoReactivate = "System roles cannot be reactivated.";

    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserId Administrator = new(Guid.Parse("a0000000-0000-4000-8000-000000000001"));

    // ------------------------------------------------------- the transition

    [Fact]
    public void Deactivating_an_active_tenant_role_is_a_change()
    {
        var role = Tenant();

        Assert.True(role.Deactivate());
        Assert.False(role.IsActive);
    }

    [Fact]
    public void Reactivating_an_inactive_tenant_role_is_a_change()
    {
        var role = Inactive();

        Assert.True(role.Reactivate());
        Assert.True(role.IsActive);
    }

    /// <summary>The pair is symmetric: round-tripping returns the role to where it began.</summary>
    [Fact]
    public void The_pair_round_trips()
    {
        var role = Tenant();

        Assert.True(role.Deactivate());
        Assert.True(role.Reactivate());

        Assert.True(role.IsActive);
    }

    // ------------------------------------------------------------ RD4: no-op

    [Fact]
    public void Deactivating_an_inactive_role_changes_nothing()
    {
        var role = Inactive();

        Assert.False(role.Deactivate());
        Assert.False(role.IsActive);
    }

    [Fact]
    public void Reactivating_an_active_role_changes_nothing()
    {
        var role = Tenant();

        Assert.False(role.Reactivate());
        Assert.True(role.IsActive);
    }

    /// <summary>Repeating it never starts refusing: idempotence is not a one-shot allowance.</summary>
    [Fact]
    public void Repeating_either_call_keeps_answering_false()
    {
        var role = Tenant();

        Assert.True(role.Deactivate());
        Assert.False(role.Deactivate());
        Assert.False(role.Deactivate());

        Assert.False(role.IsActive);
    }

    /// <summary>RD4, stated as an absence: the old refusals are gone.</summary>
    [Fact]
    public void An_already_reached_state_raises_nothing_at_all()
    {
        Inactive().Deactivate();
        Tenant().Reactivate();
    }

    // -------------------------------------------- RD-A5: ownership is first

    [Fact]
    public void A_system_role_cannot_be_deactivated()
    {
        var role = Seeded(active: true);

        Assert.Equal(NoDeactivate, Refusal(role.Deactivate));
        Assert.True(role.IsActive);
    }

    [Fact]
    public void A_system_role_cannot_be_reactivated()
    {
        var role = Seeded(active: false);

        Assert.Equal(NoReactivate, Refusal(role.Reactivate));
        Assert.False(role.IsActive);
    }

    /// <summary>
    /// The ordering test. A system role ALREADY in the target state must read
    /// as an ownership refusal, not as a no-op success — otherwise the answer
    /// says the edit was permitted and merely unnecessary.
    /// </summary>
    [Fact]
    public void A_system_role_is_refused_even_when_it_is_already_in_the_target_state()
    {
        Assert.Equal(NoDeactivate, Refusal(Seeded(active: false).Deactivate));
        Assert.Equal(NoReactivate, Refusal(Seeded(active: true).Reactivate));
    }

    // -------------------------------------------- what the lifecycle cannot move

    /// <summary>Only IsActive moves. The code, the name, the description and ownership do not.</summary>
    [Fact]
    public void Nothing_but_the_activity_flag_changes()
    {
        var role = Role.Create(
            RoleId.New(), "Quality Reviewer", "quality-reviewer", "Reviews access.",
            isSystemRole: false, Now, Administrator);

        Assert.True(role.Deactivate());

        Assert.Equal("quality-reviewer", role.Code);
        Assert.Equal("Quality Reviewer", role.Name);
        Assert.Equal("Reviews access.", role.Description);
        Assert.False(role.IsSystemRole);
    }

    /// <summary>RD-A10: an inactive role is still the metadata command's to correct.</summary>
    [Fact]
    public void An_inactive_role_can_still_have_its_metadata_changed()
    {
        var role = Inactive();

        Assert.True(role.UpdateMetadata("Access Reviewer", "Reviews everything."));

        Assert.Equal("Access Reviewer", role.Name);
        Assert.False(role.IsActive);
    }

    // ------------------------------------------------------------- harness

    private static string Refusal(Func<bool> action)
        => Assert.Throws<DomainException>(() => action()).Message;

    private static Role Tenant()
        => Role.Create(
            RoleId.New(), "Quality Reviewer", "quality-reviewer", null,
            isSystemRole: false, Now, Administrator);

    private static Role Inactive()
    {
        var role = Tenant();

        role.Deactivate();

        return role;
    }

    private static Role Seeded(bool active)
    {
        var role = Role.Create(
            RoleId.New(), "Access Reviewer", "access-reviewer", null,
            isSystemRole: true, Now, Administrator);

        // A seeded role is created active and the domain refuses to retire
        // one, so the ordering test's fixture — a system role ALREADY
        // inactive — cannot be built through the API under test. Reaching
        // past it is the point: the state is staged, and the refusal is
        // still what must come out. The database half is staged with SQL,
        // in RoleLifecycleIntegrationTests.
        if (!active)
            typeof(Role).GetProperty(nameof(Role.IsActive))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(role, [(object)false]);

        return role;
    }
}
