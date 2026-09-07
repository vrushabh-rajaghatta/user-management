using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Domain.Tests;

/// <summary>
/// Inv. 31 — tenants may strengthen the release baseline, never weaken it —
/// with the direction declared per setting.
///
/// The spec calls this out as the dangerous ambiguity in the design: "for a
/// minimum password length, stricter means a higher number. For a session idle
/// timeout, stricter means a lower one. An implementation that applied a single
/// arithmetic direction would enforce half the settings backwards." So every
/// setting is tested individually and in both directions. A test that only
/// checked "the stricter value wins" in aggregate would pass with three
/// settings inverted.
///
/// Note these tests never use SecurityBaseline.Current as the tenant value. A
/// freshly provisioned tenant is seeded FROM the baseline, which makes
/// EffectiveAgainst a no-op on day one — a suite built on seeded values would
/// pass while exercising nothing.
/// </summary>
public sealed class SecurityPolicySettingsTests
{
    /// <summary>
    /// A deliberately distinctive baseline: every value differs from every
    /// other, so a copy-paste error between settings cannot go unnoticed.
    /// </summary>
    private static readonly SecurityPolicySettings Baseline =
        new(
            PasswordMinLength: 12,
            PasswordHistoryDepth: 10,
            LockoutDuration: TimeSpan.FromMinutes(15),
            MaxFailedLoginAttempts: 5,
            ActivationTokenLifetime: TimeSpan.FromHours(72),
            PasswordResetTokenLifetime: TimeSpan.FromHours(1),
            SessionIdleTimeout: TimeSpan.FromMinutes(20),
            SessionAbsoluteTimeout: TimeSpan.FromHours(12));

    // ---------------------------------------------------------------- floors
    // A tenant below the floor is RAISED to it; above the floor it is kept.

    [Fact]
    public void PasswordMinLength_is_a_floor()
    {
        Assert.Equal(
            12,
            (Baseline with { PasswordMinLength = 4 })
                .EffectiveAgainst(Baseline).PasswordMinLength);

        Assert.Equal(
            20,
            (Baseline with { PasswordMinLength = 20 })
                .EffectiveAgainst(Baseline).PasswordMinLength);
    }

    [Fact]
    public void PasswordHistoryDepth_is_a_floor()
    {
        Assert.Equal(
            10,
            (Baseline with { PasswordHistoryDepth = 2 })
                .EffectiveAgainst(Baseline).PasswordHistoryDepth);

        Assert.Equal(
            24,
            (Baseline with { PasswordHistoryDepth = 24 })
                .EffectiveAgainst(Baseline).PasswordHistoryDepth);
    }

    [Fact]
    public void LockoutDuration_is_a_floor()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            (Baseline with { LockoutDuration = TimeSpan.FromMinutes(1) })
                .EffectiveAgainst(Baseline).LockoutDuration);

        Assert.Equal(
            TimeSpan.FromHours(2),
            (Baseline with { LockoutDuration = TimeSpan.FromHours(2) })
                .EffectiveAgainst(Baseline).LockoutDuration);
    }

    // ------------------------------------------------------------------ caps
    // A tenant above the cap is REDUCED to it; below the cap it is kept.

    [Fact]
    public void MaxFailedLoginAttempts_is_a_cap()
    {
        Assert.Equal(
            5,
            (Baseline with { MaxFailedLoginAttempts = 50 })
                .EffectiveAgainst(Baseline).MaxFailedLoginAttempts);

        Assert.Equal(
            3,
            (Baseline with { MaxFailedLoginAttempts = 3 })
                .EffectiveAgainst(Baseline).MaxFailedLoginAttempts);
    }

    [Fact]
    public void ActivationTokenLifetime_is_a_cap()
    {
        Assert.Equal(
            TimeSpan.FromHours(72),
            (Baseline with { ActivationTokenLifetime = TimeSpan.FromDays(30) })
                .EffectiveAgainst(Baseline).ActivationTokenLifetime);

        Assert.Equal(
            TimeSpan.FromHours(4),
            (Baseline with { ActivationTokenLifetime = TimeSpan.FromHours(4) })
                .EffectiveAgainst(Baseline).ActivationTokenLifetime);
    }

    [Fact]
    public void PasswordResetTokenLifetime_is_a_cap()
    {
        Assert.Equal(
            TimeSpan.FromHours(1),
            (Baseline with { PasswordResetTokenLifetime = TimeSpan.FromDays(7) })
                .EffectiveAgainst(Baseline).PasswordResetTokenLifetime);

        Assert.Equal(
            TimeSpan.FromMinutes(15),
            (Baseline with { PasswordResetTokenLifetime = TimeSpan.FromMinutes(15) })
                .EffectiveAgainst(Baseline).PasswordResetTokenLifetime);
    }

    /// <summary>
    /// The spec's own worked example of the failure this design prevents: a
    /// tenant configuring an eight-hour idle timeout must not prevail over the
    /// product's limit.
    /// </summary>
    [Fact]
    public void SessionIdleTimeout_is_a_cap()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(20),
            (Baseline with { SessionIdleTimeout = TimeSpan.FromHours(8) })
                .EffectiveAgainst(Baseline).SessionIdleTimeout);

        Assert.Equal(
            TimeSpan.FromMinutes(5),
            (Baseline with { SessionIdleTimeout = TimeSpan.FromMinutes(5) })
                .EffectiveAgainst(Baseline).SessionIdleTimeout);
    }

    [Fact]
    public void SessionAbsoluteTimeout_is_a_cap()
    {
        Assert.Equal(
            TimeSpan.FromHours(12),
            (Baseline with { SessionAbsoluteTimeout = TimeSpan.FromDays(14) })
                .EffectiveAgainst(Baseline).SessionAbsoluteTimeout);

        Assert.Equal(
            TimeSpan.FromHours(8),
            (Baseline with { SessionAbsoluteTimeout = TimeSpan.FromHours(8) })
                .EffectiveAgainst(Baseline).SessionAbsoluteTimeout);
    }

    // --------------------------------------------------------------- wholesale

    /// <summary>
    /// A tenant that weakens EVERY setting at once gets the baseline back
    /// verbatim. This is the test that catches a single inverted direction
    /// hiding among seven correct ones.
    /// </summary>
    [Fact]
    public void A_tenant_weakening_everything_is_clamped_to_the_baseline_exactly()
    {
        var weakened = new SecurityPolicySettings(
            PasswordMinLength: 4,
            PasswordHistoryDepth: 0,
            LockoutDuration: TimeSpan.FromSeconds(1),
            MaxFailedLoginAttempts: 1_000,
            ActivationTokenLifetime: TimeSpan.FromDays(365),
            PasswordResetTokenLifetime: TimeSpan.FromDays(365),
            SessionIdleTimeout: TimeSpan.FromHours(8),
            SessionAbsoluteTimeout: TimeSpan.FromDays(30));

        Assert.Equal(Baseline, weakened.EffectiveAgainst(Baseline));
    }

    /// <summary>
    /// A tenant that strengthens every setting keeps all of them — the baseline
    /// is a bound, not an override.
    /// </summary>
    [Fact]
    public void A_tenant_strengthening_everything_is_left_untouched()
    {
        var strengthened = new SecurityPolicySettings(
            PasswordMinLength: 16,
            PasswordHistoryDepth: 24,
            LockoutDuration: TimeSpan.FromHours(1),
            MaxFailedLoginAttempts: 3,
            ActivationTokenLifetime: TimeSpan.FromHours(8),
            PasswordResetTokenLifetime: TimeSpan.FromMinutes(30),
            SessionIdleTimeout: TimeSpan.FromMinutes(10),
            SessionAbsoluteTimeout: TimeSpan.FromHours(4));

        Assert.Equal(strengthened, strengthened.EffectiveAgainst(Baseline));
    }

    [Fact]
    public void Values_already_equal_to_the_baseline_are_unchanged()
    {
        Assert.Equal(Baseline, Baseline.EffectiveAgainst(Baseline));
    }

    /// <summary>
    /// Clamping reads the tenant's values, it does not mutate them. The stored
    /// policy stays exactly as configured; only the computed answer differs.
    /// </summary>
    [Fact]
    public void The_tenant_settings_are_not_mutated()
    {
        var tenant = Baseline with { SessionIdleTimeout = TimeSpan.FromHours(8) };

        tenant.EffectiveAgainst(Baseline);

        Assert.Equal(TimeSpan.FromHours(8), tenant.SessionIdleTimeout);
    }

    [Fact]
    public void A_null_baseline_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => Baseline.EffectiveAgainst(null!));
    }

    // ------------------------------------------------------- the baseline itself

    /// <summary>
    /// PlatformProvisioner seeds a new tenant from SecurityBaseline.Current, so
    /// a freshly provisioned tenant must resolve to the baseline unchanged. If
    /// this ever fails, seeding and evaluation have drifted apart.
    /// </summary>
    [Fact]
    public void A_freshly_seeded_tenant_resolves_to_the_baseline()
    {
        Assert.Equal(
            SecurityBaseline.Current,
            SecurityBaseline.Current.EffectiveAgainst(SecurityBaseline.Current));
    }
}
