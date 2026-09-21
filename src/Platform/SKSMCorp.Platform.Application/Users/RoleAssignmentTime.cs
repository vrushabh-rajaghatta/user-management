namespace SKSMCorp.Platform.Application.Users;

/// <summary>
/// PostgreSQL keeps timestamps to the microsecond; DateTimeOffset carries
/// 100-nanosecond ticks. A period is therefore judged at the precision it is
/// STORED at. Otherwise a grant whose end is one tick after its start would
/// pass the strict "ends after it starts" rule and then be stored as an empty
/// period, which the database admits (frozen UR2) because revocation needs it.
/// </summary>
internal static class RoleAssignmentTime
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    internal static DateTimeOffset AtStoredPrecision(DateTimeOffset value)
        => value.AddTicks(-(value.Ticks % TicksPerMicrosecond));

    internal static DateTimeOffset? AtStoredPrecision(DateTimeOffset? value)
        => value is null ? null : AtStoredPrecision(value.Value);
}
