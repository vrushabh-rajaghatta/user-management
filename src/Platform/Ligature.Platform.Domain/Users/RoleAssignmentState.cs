namespace Ligature.Platform.Domain.Users;

/// <summary>
/// AUT-Q2. RED STUB: the contract is in docs/requirements.md ("AUT-Q2"), and
/// these exist only so the tests that describe them compile.
/// </summary>
public enum RoleAssignmentState
{
    Active,
    Future,
    Ended,
    Revoked,
}

/// <summary>AUT-Q2. RED STUB.</summary>
public static class RoleAssignmentStates
{
    public static RoleAssignmentState At(
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        DateTimeOffset? revokedAt,
        DateTimeOffset now)
        => throw new NotImplementedException("AUT-Q2 is not implemented.");
}
