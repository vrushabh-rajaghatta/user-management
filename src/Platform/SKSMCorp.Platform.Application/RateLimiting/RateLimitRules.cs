namespace SKSMCorp.Platform.Application.RateLimiting;

/// <summary>
/// The frozen limits (B4), as release constants (B5). Not configuration and
/// not security_policy: changing one is a release, and a change control on the
/// contract.
/// </summary>
public static class RateLimitRules
{
    public static readonly RateLimitRule SignInByUsername =
        new("sign-in", RateLimitKeyKind.Address, 10, TimeSpan.FromMinutes(15));

    public static readonly RateLimitRule SignInByClientAddress =
        new("sign-in", RateLimitKeyKind.ClientAddress, 30, TimeSpan.FromMinutes(1));

    public static readonly RateLimitRule PasswordResetRequestByAddress =
        new("password-reset-request", RateLimitKeyKind.Address, 3, TimeSpan.FromHours(1));

    public static readonly RateLimitRule PasswordResetRequestByClientAddress =
        new("password-reset-request", RateLimitKeyKind.ClientAddress, 10, TimeSpan.FromHours(1));

    public static readonly RateLimitRule ResetPasswordByClientAddress =
        new("reset-password", RateLimitKeyKind.ClientAddress, 10, TimeSpan.FromMinutes(15));

    public static readonly RateLimitRule ActivateAccountByClientAddress =
        new("activate", RateLimitKeyKind.ClientAddress, 10, TimeSpan.FromMinutes(15));
}
