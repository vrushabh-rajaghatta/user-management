namespace SKSMCorp.Platform.Domain.Users;

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
    /// In this order, and REVOCATION WINS FROM THE MOMENT IT HAPPENED: a
    /// future grant cancelled before it started has an empty period that has
    /// "passed", but it never lapsed, so it is Revoked and not Ended. The
    /// period is half-open, [EffectiveFrom, EffectiveTo), exactly as the
    /// authorisation check reads it (UR12), so the state never disagrees with
    /// whether it authorises.
    ///
    /// REVOCATION IS NOT RETROACTIVE, and the comparison against the instant is
    /// what says so. Asked about an instant BEFORE the revocation, this answers
    /// what was true then — the holder held the role — rather than projecting
    /// today's decision back over history. At the current instant the two
    /// readings are identical, because a revocation is stamped when it happens
    /// and cannot be in the future; the difference appears only when a caller
    /// asks about the past, which AUT-Q4 is the first to do.
    ///
    /// Reconstructing the past with today's configuration is the specific
    /// failure the effective-dating exists to prevent (spec §1.2), and an
    /// access review that showed nobody as ever having held a since-revoked
    /// role would be evidence of exactly nothing.
    /// </summary>
    public static RoleAssignmentState At(
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        DateTimeOffset? revokedAt,
        DateTimeOffset now)
    {
        if (revokedAt is not null && revokedAt <= now)
            return RoleAssignmentState.Revoked;

        if (now < effectiveFrom)
            return RoleAssignmentState.Future;

        if (effectiveTo is not null && now >= effectiveTo)
            return RoleAssignmentState.Ended;

        return RoleAssignmentState.Active;
    }
}
