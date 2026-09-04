namespace Ligature.Platform.Domain.Users;

public sealed record SecurityPolicySettings(
    int PasswordMinLength,
    int PasswordHistoryDepth,
    TimeSpan LockoutDuration,
    int MaxFailedLoginAttempts,
    TimeSpan ActivationTokenLifetime,
    TimeSpan PasswordResetTokenLifetime,
    TimeSpan SessionIdleTimeout,
    TimeSpan SessionAbsoluteTimeout)
{
    public SecurityPolicySettings EffectiveAgainst(
        SecurityPolicySettings baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        return new SecurityPolicySettings(
            PasswordMinLength:
                Math.Max(
                    PasswordMinLength,
                    baseline.PasswordMinLength),

            PasswordHistoryDepth:
                Math.Max(
                    PasswordHistoryDepth,
                    baseline.PasswordHistoryDepth),

            LockoutDuration:
                Max(
                    LockoutDuration,
                    baseline.LockoutDuration),

            MaxFailedLoginAttempts:
                Math.Min(
                    MaxFailedLoginAttempts,
                    baseline.MaxFailedLoginAttempts),

            ActivationTokenLifetime:
                Min(
                    ActivationTokenLifetime,
                    baseline.ActivationTokenLifetime),

            PasswordResetTokenLifetime:
                Min(
                    PasswordResetTokenLifetime,
                    baseline.PasswordResetTokenLifetime),

            SessionIdleTimeout:
                Min(
                    SessionIdleTimeout,
                    baseline.SessionIdleTimeout),

            SessionAbsoluteTimeout:
                Min(
                    SessionAbsoluteTimeout,
                    baseline.SessionAbsoluteTimeout));
    }

    private static TimeSpan Max(
        TimeSpan left,
        TimeSpan right)
        => left >= right ? left : right;

    private static TimeSpan Min(
        TimeSpan left,
        TimeSpan right)
        => left <= right ? left : right;
}