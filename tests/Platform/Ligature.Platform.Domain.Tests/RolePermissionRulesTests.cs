using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Tests;

/// <summary>
/// The grant's own lifecycle (docs/requirements.md, "AUT-C7
/// AddPermissionToRole and AUT-C8 RemovePermissionFromRole").
///
/// GREEN ON ARRIVAL, AND THAT IS THE POINT. This story changes the domain not
/// at all: RolePermission.Create and RolePermission.Revoke already say
/// everything the pair needs, and until now Revoke had never been called by
/// anything. These pin what AUT-C8 is about to depend on, before it does.
///
/// Note what is NOT here: RP6, ownership, the live-grant rule and the
/// permission's activity. None can live in the domain — each needs a row this
/// aggregate cannot see — so all four are the handler's (RG2, RG7).
/// </summary>
public sealed class RolePermissionRulesTests
{
    private static readonly DateTimeOffset Granted = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserId Administrator = new(Guid.Parse("a0000000-0000-4000-8000-000000000001"));

    [Fact]
    public void A_new_grant_is_live()
    {
        var grant = Create();

        Assert.True(grant.IsActive);
        Assert.Null(grant.RevokedAt);
        Assert.Null(grant.RevokedBy);
    }

    [Fact]
    public void Revoking_closes_the_row_and_records_who_and_when()
    {
        var grant = Create();
        var at = Granted.AddDays(1);

        grant.Revoke(at, Administrator);

        Assert.False(grant.IsActive);
        Assert.Equal(at, grant.RevokedAt);
        Assert.Equal(Administrator, grant.RevokedBy);
    }

    /// <summary>The row is closed, never emptied: what it granted, and when, survives.</summary>
    [Fact]
    public void Revoking_leaves_the_grant_itself_intact()
    {
        var grant = Create();
        var roleId = grant.RoleId;
        var permissionId = grant.PermissionId;

        grant.Revoke(Granted.AddDays(1), Administrator);

        Assert.Equal(roleId, grant.RoleId);
        Assert.Equal(permissionId, grant.PermissionId);
        Assert.Equal(Granted, grant.GrantedAt);
    }

    [Fact]
    public void A_second_revocation_is_refused()
    {
        var grant = Create();

        grant.Revoke(Granted.AddDays(1), Administrator);

        Assert.Equal(
            "Role permission has already been revoked.",
            Assert.Throws<DomainException>(() => grant.Revoke(Granted.AddDays(2), Administrator)).Message);
    }

    [Fact]
    public void A_revocation_before_the_grant_is_refused()
        => Assert.Equal(
            "Permission revocation cannot occur before the grant.",
            Assert.Throws<DomainException>(() => Create().Revoke(Granted.AddDays(-1), Administrator)).Message);

    /// <summary>RP1: a re-grant is a NEW row, so two grants for one pair coexist.</summary>
    [Fact]
    public void A_re_grant_is_a_separate_row()
    {
        var first = Create();

        first.Revoke(Granted.AddDays(1), Administrator);

        var second = RolePermission.Create(
            RolePermissionId.New(), first.RoleId, first.PermissionId, Granted.AddDays(2), Administrator);

        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.IsActive);
        Assert.True(second.IsActive);
    }

    private static RolePermission Create()
        => RolePermission.Create(
            RolePermissionId.New(), RoleId.New(), PermissionId.New(), Granted, Administrator);
}
