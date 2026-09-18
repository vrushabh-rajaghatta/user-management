using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Tests;

/// <summary>
/// USR-C4 / USR-C5 in the domain (docs/requirements.md, "USR-C4 / USR-C5").
///
/// Deactivation stamps the user and each identity with one (at, by) pair, and
/// reactivation returns only the identities carrying the user's own stamp, so
/// an identity deactivated for another reason stays inactive.
/// </summary>
public sealed class UserLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserId Administrator = new(Guid.Parse("a0000000-0000-4000-8000-000000000001"));

    // ------------------------------------------------------------ the user

    [Fact]
    public void Deactivation_makes_the_user_inactive_and_records_who_and_when()
    {
        var user = Human();
        var stamp = new DeactivationStamp(Now, Administrator);

        user.Deactivate(stamp);

        Assert.Equal(UserStatus.Inactive, user.Status);
        Assert.Equal(stamp, user.Deactivation);
    }

    /// <summary>D9c — stated as exactly this, and nothing about administrators.</summary>
    [Fact]
    public void A_user_cannot_deactivate_themselves()
    {
        var user = Human();

        var refusal = Assert.Throws<DomainException>(() => user.Deactivate(new DeactivationStamp(Now, user.Id)));

        Assert.Equal("A user cannot deactivate themselves.", refusal.Message);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.Deactivation);
    }

    [Fact]
    public void An_inactive_user_cannot_be_deactivated_again()
    {
        var user = Human();
        var first = new DeactivationStamp(Now, Administrator);
        user.Deactivate(first);

        Assert.Throws<DomainException>(() => user.Deactivate(new DeactivationStamp(Now.AddHours(1), Administrator)));
        Assert.Equal(first, user.Deactivation);
    }

    [Fact]
    public void The_system_actor_cannot_be_deactivated()
    {
        var system = User.CreateSystem(Now);

        Assert.Throws<DomainException>(() => system.Deactivate(new DeactivationStamp(Now, Administrator)));
    }

    [Fact]
    public void Reactivation_returns_the_user_to_active_and_clears_the_stamp()
    {
        var user = Human();
        user.Deactivate(new DeactivationStamp(Now, Administrator));

        user.Reactivate();

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.Deactivation);
    }

    [Fact]
    public void An_active_user_cannot_be_reactivated()
        => Assert.Throws<DomainException>(() => Human().Reactivate());

    // ------------------------------------------------------------ identities (D5)

    [Fact]
    public void An_identity_carrying_the_users_stamp_is_reactivated_with_the_user()
    {
        var stamp = new DeactivationStamp(Now, Administrator);
        var identity = Identity();
        identity.Deactivate(stamp);

        Assert.True(identity.ReactivateWith(stamp));
        Assert.Equal(UserStatus.Active, identity.Status);
        Assert.Null(identity.Deactivation);
    }

    /// <summary>
    /// A future IDN-C3 deactivates one identity on its own; reactivating the
    /// user must not undo that decision.
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void An_identity_carrying_a_different_stamp_stays_inactive(int minutesLater, bool otherAdministrator)
    {
        var userStamp = new DeactivationStamp(Now, Administrator);
        var identityStamp = new DeactivationStamp(
            Now.AddMinutes(minutesLater),
            otherAdministrator ? new UserId(Guid.Parse("a0000000-0000-4000-8000-000000000002")) : Administrator);

        var identity = Identity();
        identity.Deactivate(identityStamp);

        Assert.False(identity.ReactivateWith(userStamp));
        Assert.Equal(UserStatus.Inactive, identity.Status);
        Assert.Equal(identityStamp, identity.Deactivation);
    }

    [Fact]
    public void An_active_identity_is_left_alone()
    {
        var identity = Identity();

        Assert.False(identity.ReactivateWith(new DeactivationStamp(Now, Administrator)));
        Assert.Equal(UserStatus.Active, identity.Status);
    }

    // ------------------------------------------------------------ helpers

    private static User Human()
        => User.CreateHuman(
            new UserId(Guid.NewGuid()), "John", "Leaver", "John Leaver", "john.leaver@example.test",
            Now.AddYears(-1), User.SystemUserId);

    private static UserIdentity Identity()
        => UserIdentity.CreateLocal(
            new UserIdentityId(Guid.NewGuid()), new UserId(Guid.NewGuid()), ActorType.Human, "john.leaver",
            Now.AddYears(-1), User.SystemUserId);
}
