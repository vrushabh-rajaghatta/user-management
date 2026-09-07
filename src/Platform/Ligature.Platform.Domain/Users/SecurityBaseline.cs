namespace Ligature.Platform.Domain.Users;

/// <summary>
/// The mandatory security floor and ceiling owned by the application release
/// (inv. 31).
///
/// It lives in code, not in configuration and not in the tenant database, and
/// that placement is the control. A tenant administrator able to reach these
/// values could reduce the minimum password length to four and the idle timeout
/// to eight hours — and the product is validated on the claim that it enforces
/// certain standards. Anything editable outside a release would make that claim
/// false. Being a compiled constant also makes the baseline part of the
/// validated release artifact rather than of a deployment's environment.
///
/// This type is the SINGLE source of these eight values. PlatformProvisioner
/// seeds a new tenant's first policy version from it, and SecurityPolicyResolver
/// clamps every tenant policy against it, which gives a useful invariant: the
/// values a brand-new tenant is seeded with are exactly the baseline it is
/// subsequently evaluated against. The dependency runs one way — nothing here
/// knows about provisioning.
///
/// A release that TIGHTENS the baseline takes effect on deployment, because
/// effective values are computed at evaluation time rather than at write time.
/// A tenant left holding a now-non-compliant stored value is enforced correctly
/// immediately; nobody has to act first.
///
/// Any setting added in future must declare its direction — floor or cap — in
/// the same release that introduces it. See
/// <see cref="SecurityPolicySettings.EffectiveAgainst"/>.
/// </summary>
public static class SecurityBaseline
{
    /// <summary>
    /// Floors are minimums a tenant may raise; caps are maximums a tenant may
    /// lower. Neither may be weakened.
    /// </summary>
    public static SecurityPolicySettings Current { get; } =
        new(
            // Floor — a tenant may demand longer passwords, never shorter.
            PasswordMinLength: 12,

            // Floor — a tenant may bar more previous passwords, never fewer.
            PasswordHistoryDepth: 10,

            // Floor — a tenant may lock out for longer, never briefer.
            LockoutDuration: TimeSpan.FromMinutes(15),

            // Cap — a tenant may tolerate fewer failures, never more.
            MaxFailedLoginAttempts: 5,

            // Cap — a tenant may expire activation links sooner, never later.
            ActivationTokenLifetime: TimeSpan.FromHours(72),

            // Cap — a tenant may expire reset links sooner, never later.
            PasswordResetTokenLifetime: TimeSpan.FromHours(1),

            // Cap — a tenant may sign idle users out sooner, never later.
            SessionIdleTimeout: TimeSpan.FromMinutes(15),

            // Cap — a tenant may cut absolute session length, never extend it.
            SessionAbsoluteTimeout: TimeSpan.FromHours(12));
}
