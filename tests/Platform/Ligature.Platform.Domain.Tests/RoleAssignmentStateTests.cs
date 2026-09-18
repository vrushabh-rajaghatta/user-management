using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Domain.Tests;

/// <summary>
/// AUT-Q2's state, derived at read time and never stored (docs/requirements.md,
/// "AUT-Q2"). In order, and revocation wins:
///
///   Revoked   RevokedAt is set
///   Future    EffectiveFrom > now
///   Ended     EffectiveTo <= now
///   Active    otherwise
///
/// The period is half-open, [EffectiveFrom, EffectiveTo), exactly as the
/// authorisation check reads it (UR12), so the state never disagrees with
/// whether the assignment authorises.
/// </summary>
public sealed class RoleAssignmentStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Before_its_start_an_assignment_is_future()
        => Assert.Equal(RoleAssignmentState.Future, State(Start, null, null, Start.AddTicks(-1)));

    [Fact]
    public void At_its_start_an_assignment_is_active()
        => Assert.Equal(RoleAssignmentState.Active, State(Start, null, null, Start));

    [Fact]
    public void An_open_ended_assignment_stays_active()
        => Assert.Equal(RoleAssignmentState.Active, State(Start, null, null, Start.AddYears(5)));

    [Fact]
    public void Just_before_its_end_an_assignment_is_active()
        => Assert.Equal(RoleAssignmentState.Active, State(Start, Start.AddDays(30), null, Start.AddDays(30).AddTicks(-1)));

    /// <summary>Half-open: at its end it no longer authorises, so it has ended.</summary>
    [Fact]
    public void At_its_end_an_assignment_has_ended()
        => Assert.Equal(RoleAssignmentState.Ended, State(Start, Start.AddDays(30), null, Start.AddDays(30)));

    /// <summary>One assignment read at three instants: Future, then Active, then Ended.</summary>
    [Fact]
    public void One_assignment_moves_from_future_to_active_to_ended()
    {
        var end = Start.AddDays(30);

        Assert.Equal(
            [RoleAssignmentState.Future, RoleAssignmentState.Active, RoleAssignmentState.Ended],
            new[] { Start.AddDays(-1), Start.AddDays(1), end.AddDays(1) }
                .Select(now => State(Start, end, null, now)));
    }

    /// <summary>A revoked active assignment reads Revoked, not Ended.</summary>
    [Fact]
    public void A_revoked_assignment_is_revoked_even_after_its_period()
    {
        var revokedAt = Start.AddDays(3);

        Assert.Equal(RoleAssignmentState.Revoked, State(Start, revokedAt, revokedAt, Start.AddDays(4)));
    }

    /// <summary>
    /// The cancelled future grant: an empty period, revoked before it started.
    /// Revoked at every instant: before its start, at it, and long after. It
    /// never authorised, and must not read as having lapsed.
    /// </summary>
    [Fact]
    public void A_future_grant_revoked_before_it_starts_is_revoked_at_every_instant()
    {
        var revokedAt = Start.AddDays(-13);

        Assert.All(
            new[] { revokedAt, Start.AddDays(-1), Start, Start.AddDays(1), Start.AddYears(1) },
            now => Assert.Equal(RoleAssignmentState.Revoked, State(Start, Start, revokedAt, now)));
    }

    /// <summary>The same derivation, through the entity.</summary>
    [Fact]
    public void A_UserRole_reports_the_same_state()
    {
        var assignedAt = Start.AddDays(-2);

        var assignment = UserRole.Create(
            UserRoleId.New(), UserId.New(), ActorType.Human, RoleId.New(),
            ScopeType.Global, scopeId: null,
            effectiveFrom: Start, effectiveTo: null,
            assignedAt: assignedAt, assignedBy: UserId.New(), assignmentReason: "Future grant.",
            createdAt: assignedAt, createdBy: UserId.New());

        Assert.Equal(RoleAssignmentState.Future, assignment.StateAt(Start.AddDays(-1)));

        assignment.Revoke(Start.AddDays(-1), UserId.New(), "Cancelled.");

        Assert.Equal(RoleAssignmentState.Revoked, assignment.StateAt(Start.AddDays(1)));
    }

    private static RoleAssignmentState State(
        DateTimeOffset from, DateTimeOffset? to, DateTimeOffset? revokedAt, DateTimeOffset now)
        => RoleAssignmentStates.At(from, to, revokedAt, now);
}
