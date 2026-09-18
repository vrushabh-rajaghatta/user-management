namespace Ligature.Platform.Domain.Users;

/// <summary>
/// Where a role assignment stands at an instant (AUT-Q2). Derived from the
/// stored facts, never stored itself: user_role has no status column, and a
/// stored one would eventually disagree with the dates (spec §6.11).
/// </summary>
public enum RoleAssignmentState
{
    Active,
    Future,
    Ended,
    Revoked,
}

/// <summary>
/// THE derivation of <see cref="RoleAssignmentState"/>, and the only one.
/// UserRole.StateAt uses it, and so does the read, which holds stored records
/// rather than entities. Nothing else — no repository, mapper or client —
/// decides an assignment's state.
/// </summary>
public static class RoleAssignmentStates
{
    /// <summary>
    /// In this order, and REVOCATION WINS: a future grant cancelled before it
    /// started has an empty period that has "passed", but it never lapsed, so
    /// it is Revoked and not Ended. The period is half-open,
    /// [EffectiveFrom, EffectiveTo), exactly as the authorisation check reads
    /// it (UR12), so the state never disagrees with whether it authorises.
    /// </summary>
    public static RoleAssignmentState At(
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        DateTimeOffset? revokedAt,
        DateTimeOffset now)
    {
        if (revokedAt is not null)
            return RoleAssignmentState.Revoked;

        if (now < effectiveFrom)
            return RoleAssignmentState.Future;

        if (effectiveTo is not null && now >= effectiveTo)
            return RoleAssignmentState.Ended;

        return RoleAssignmentState.Active;
    }
}
