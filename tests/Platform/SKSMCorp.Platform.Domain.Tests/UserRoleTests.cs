using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Domain.Tests;

/// <summary>
/// AUT-C1 / AUT-C2 in the domain (docs/requirements.md, "Role Assignment").
///
/// Three rules about the effective period, deliberately different:
///
///   database (frozen UR2)   EffectiveTo >= EffectiveFrom — admits the empty period
///   a grant                 EffectiveTo >  EffectiveFrom — must be able to authorise
///   revoking a future grant EffectiveTo := EffectiveFrom — the empty period, on purpose
///
/// And the grant date is not the effective date: a grant may take effect
/// later than it was made, never earlier.
/// </summary>
public sealed class UserRoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private const string Reason = "Onboarding, regulatory affairs; ticket REQ-1042.";

    // ------------------------------------------------------------ granting

    [Fact]
    public void A_grant_may_be_open_ended()
    {
        var assignment = Grant(effectiveFrom: Now, effectiveTo: null);

        Assert.Equal(Now, assignment.EffectiveFrom);
        Assert.Null(assignment.EffectiveTo);
    }

    [Fact]
    public void A_grant_may_end_after_it_starts()
    {
        var assignment = Grant(effectiveFrom: Now, effectiveTo: Now.AddMonths(2));

        Assert.Equal(Now.AddMonths(2), assignment.EffectiveTo);
    }

    /// <summary>
    /// A grant made today may take effect next month: the decision and its
    /// effect are recorded separately.
    /// </summary>
    [Fact]
    public void A_grant_may_take_effect_after_it_was_made()
    {
        var assignment = Grant(effectiveFrom: Now.AddDays(13), effectiveTo: null);

        Assert.Equal(Now, assignment.AssignedAt);
        Assert.Equal(Now.AddDays(13), assignment.EffectiveFrom);
    }

    /// <summary>
    /// No backdating: an assignment that took effect before anyone decided to
    /// grant it would record access as valid that nobody had granted.
    /// </summary>
    [Fact]
    public void A_grant_cannot_take_effect_before_it_was_made()
    {
        Assert.Throws<DomainException>(() => Grant(effectiveFrom: Now.AddTicks(-1), effectiveTo: null));
    }

    /// <summary>
    /// The database admits equal dates (UR2) so that revocation can produce an
    /// empty period. A GRANT must not: an assignment that can never authorise
    /// records a decision with no effect.
    /// </summary>
    [Fact]
    public void A_grant_cannot_create_an_empty_period()
    {
        Assert.Throws<DomainException>(() => Grant(effectiveFrom: Now.AddDays(13), effectiveTo: Now.AddDays(13)));
    }

    [Fact]
    public void A_grant_cannot_end_before_it_starts()
    {
        Assert.Throws<DomainException>(() => Grant(effectiveFrom: Now.AddDays(13), effectiveTo: Now.AddDays(12)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_grant_needs_a_reason(string reason)
    {
        Assert.Throws<DomainException>(() => Grant(effectiveFrom: Now, effectiveTo: null, reason: reason));
    }

    // ------------------------------------------------------------ revoking

    /// <summary>An active assignment stops authorising when it is revoked.</summary>
    [Fact]
    public void Revoking_an_active_assignment_ends_it_at_the_revocation_time()
    {
        var assignment = Grant(effectiveFrom: Now, effectiveTo: null);
        var revoker = UserId.New();

        assignment.Revoke(Now.AddDays(3), revoker, "Left the regulatory team.");

        Assert.Equal(Now.AddDays(3), assignment.EffectiveTo);
        Assert.Equal(Now.AddDays(3), assignment.RevokedAt);
        Assert.Equal(revoker, assignment.RevokedBy);
        Assert.Equal("Left the regulatory team.", assignment.RevocationReason);
    }

    /// <summary>Revoking only ever brings an end forward.</summary>
    [Fact]
    public void Revoking_an_active_assignment_with_a_later_end_brings_the_end_forward()
    {
        var assignment = Grant(effectiveFrom: Now, effectiveTo: Now.AddMonths(2));

        assignment.Revoke(Now.AddDays(3), UserId.New(), "Project ended early.");

        Assert.Equal(Now.AddDays(3), assignment.EffectiveTo);
    }

    /// <summary>
    /// A future assignment is closed before it opens: EffectiveTo becomes its
    /// own EffectiveFrom, an empty period that never authorises and overlaps
    /// nothing. Not the revocation time, which would put the end before the
    /// start.
    /// </summary>
    [Fact]
    public void Revoking_a_future_assignment_closes_it_at_its_start()
    {
        var assignment = Grant(effectiveFrom: Now.AddDays(13), effectiveTo: null);

        assignment.Revoke(Now.AddDays(1), UserId.New(), "Offer withdrawn.");

        Assert.Equal(Now.AddDays(13), assignment.EffectiveTo);
        Assert.Equal(assignment.EffectiveFrom, assignment.EffectiveTo);
        Assert.Equal(Now.AddDays(1), assignment.RevokedAt);
    }

    [Fact]
    public void Revoking_a_future_assignment_with_an_end_closes_it_at_its_start()
    {
        var assignment = Grant(effectiveFrom: Now.AddDays(13), effectiveTo: Now.AddMonths(3));

        assignment.Revoke(Now.AddDays(1), UserId.New(), "Offer withdrawn.");

        Assert.Equal(Now.AddDays(13), assignment.EffectiveTo);
    }

    /// <summary>
    /// The boundary between future and active: an assignment starting exactly
    /// now is active, so it ends now.
    /// </summary>
    [Fact]
    public void Revoking_an_assignment_at_the_instant_it_starts_ends_it_then()
    {
        var assignment = Grant(effectiveFrom: Now.AddDays(13), effectiveTo: null);

        assignment.Revoke(Now.AddDays(13), UserId.New(), "Cancelled on the day.");

        Assert.Equal(Now.AddDays(13), assignment.EffectiveTo);
    }

    /// <summary>
    /// Nothing is left to close. Refused rather than moving the end LATER,
    /// which the database would also refuse (G4: a window never widens).
    /// </summary>
    [Fact]
    public void Revoking_an_ended_assignment_is_refused()
    {
        var assignment = Grant(effectiveFrom: Now, effectiveTo: Now.AddDays(10));

        Assert.Throws<DomainException>(() => assignment.Revoke(Now.AddDays(11), UserId.New(), "Too late."));

        Assert.Null(assignment.RevokedAt);
        Assert.Equal(Now.AddDays(10), assignment.EffectiveTo);
    }

    /// <summary>The period is half-open: at its end it has already ended.</summary>
    [Fact]
    public void Revoking_an_assignment_at_the_instant_it_ends_is_refused()
    {
        var assignment = Grant(effectiveFrom: Now, effectiveTo: Now.AddDays(10));

        Assert.Throws<DomainException>(() => assignment.Revoke(Now.AddDays(10), UserId.New(), "Exactly at the end."));
    }

    [Fact]
    public void Revoking_twice_is_refused()
    {
        var assignment = Grant(effectiveFrom: Now, effectiveTo: null);

        assignment.Revoke(Now.AddDays(1), UserId.New(), "First.");

        Assert.Throws<DomainException>(() => assignment.Revoke(Now.AddDays(2), UserId.New(), "Second."));
        Assert.Equal("First.", assignment.RevocationReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Revoking_needs_a_reason(string reason)
    {
        var assignment = Grant(effectiveFrom: Now, effectiveTo: null);

        Assert.Throws<DomainException>(() => assignment.Revoke(Now.AddDays(1), UserId.New(), reason));
        Assert.Null(assignment.RevokedAt);
    }

    // ------------------------------------------------------------ harness

    private static UserRole Grant(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, string reason = Reason)
        => UserRole.Create(
            UserRoleId.New(),
            UserId.New(),
            ActorType.Human,
            RoleId.New(),
            ScopeType.Global,
            scopeId: null,
            effectiveFrom: effectiveFrom,
            effectiveTo: effectiveTo,
            assignedAt: Now,
            assignedBy: UserId.New(),
            assignmentReason: reason,
            createdAt: Now,
            createdBy: UserId.New());
}
