using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Provisioning;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The FIRST execution coverage PRV-C1 and PRV-C3 have ever had.
///
/// Until now `ProvisionAsync` had no call sites anywhere — not in src, not in
/// tests. CatalogueDriftTests compares the static seed lists against a database
/// somebody provisioned by hand, which proves the lists match that database but
/// says nothing about whether this code can produce it. These tests run the
/// write paths against a migrated but empty database.
/// </summary>
public sealed class ProvisioningIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PRV_C1_provisions_the_platform_baseline_from_empty()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await using var context = database.CreateContext();

        var system = await new PlatformProvisioner(context)
            .ProvisionAsync(Now, CancellationToken.None);

        Assert.Equal(User.SystemUserId, system.Id);
        Assert.Equal(ActorType.System, system.ActorType);

        await using var reader = database.CreateContext();

        // Asserted against the seed lists rather than hard-coded counts, so
        // adding a permission does not require editing this test to match.
        Assert.Equal(
            PlatformProvisioner.GetPermissionSeeds().Count,
            await reader.Set<Permission>().CountAsync());

        Assert.Equal(
            PlatformProvisioner.GetRoleSeeds().Count,
            await reader.Set<Role>().CountAsync());

        Assert.Equal(
            PlatformProvisioner.GetRolePermissionSeeds().Count,
            await reader.Set<RolePermission>().CountAsync());

        Assert.Equal(1, await reader.Set<SecurityPolicy>().CountAsync());

        // The System actor is the only user, and no human exists yet.
        Assert.Equal(1, await reader.Set<User>().CountAsync());
    }

    /// <summary>
    /// The sentinel: the System actor's existence makes the whole operation a
    /// no-op, so a partially provisioned database is never mistaken for a
    /// successfully provisioned one.
    /// </summary>
    [Fact]
    public async Task PRV_C1_is_idempotent()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await using (var first = database.CreateContext())
        {
            await new PlatformProvisioner(first)
                .ProvisionAsync(Now, CancellationToken.None);
        }

        await using (var second = database.CreateContext())
        {
            await new PlatformProvisioner(second)
                .ProvisionAsync(Now.AddDays(1), CancellationToken.None);
        }

        await using var reader = database.CreateContext();

        // Not doubled.
        Assert.Equal(
            PlatformProvisioner.GetPermissionSeeds().Count,
            await reader.Set<Permission>().CountAsync());

        Assert.Equal(1, await reader.Set<SecurityPolicy>().CountAsync());
    }

    // ------------------------------------------------------------- PRV-C3

    [Fact]
    public async Task PRV_C3_provisions_the_bootstrap_administrator()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionPlatformAsync(database);

        var delivered = new List<BootstrapAdministratorResult>();

        BootstrapAdministratorResult? result;

        await using (var context = database.CreateContext())
        {
            result = await Bootstrap(context).ProvisionAsync(
                Request,
                Now,
                (x, _) => { delivered.Add(x); return Task.CompletedTask; },
                CancellationToken.None);
        }

        Assert.NotNull(result);

        // Delivered exactly once, and with the same token the caller receives.
        var single = Assert.Single(delivered);
        Assert.Equal(result!.ActivationToken, single.ActivationToken);
        Assert.False(string.IsNullOrWhiteSpace(result.ActivationToken));

        await using var reader = database.CreateContext();

        var administrator = await reader.Set<User>()
            .SingleAsync(x => x.ActorType == ActorType.Human);

        Assert.Equal(result.UserId, administrator.Id);
        Assert.Equal(UserStatus.Active, administrator.Status);

        // Both administrator roles, granted globally and open-ended.
        Assert.Equal(2, await reader.Set<UserRole>().CountAsync());

        // Inv. 15 — NO credential row. The administrator proves control of the
        // mailbox and sets their own password; provisioning must never know it,
        // or every approval they later make is contestable.
        Assert.Equal(0, await reader.Set<Credential>().CountAsync());

        // Only the hash is persisted (UT7).
        var token = await reader.Set<UserToken>().SingleAsync();
        Assert.Equal(TokenType.Activation, token.TokenType);
        Assert.DoesNotContain(
            result.ActivationToken, token.TokenHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE invariant this story exists to establish.
    ///
    /// Delivery fails, so nothing may be committed. Were the commit to happen
    /// first, an administrator would exist whose activation token is gone
    /// forever — and the sentinel means PRV-C3 will never run again to issue
    /// another, so the tenant is unrecoverable.
    ///
    /// Move the delivery call after CommitAsync and this test fails.
    /// </summary>
    [Fact]
    public async Task A_failed_delivery_commits_no_administrator()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionPlatformAsync(database);

        await using (var context = database.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Bootstrap(context).ProvisionAsync(
                    Request,
                    Now,
                    (_, _) => throw new InvalidOperationException(
                        "The token could not be made durable."),
                    CancellationToken.None));
        }

        await using var reader = database.CreateContext();

        Assert.Equal(
            0,
            await reader.Set<User>().CountAsync(x => x.ActorType == ActorType.Human));

        Assert.Equal(0, await reader.Set<UserToken>().CountAsync());
        Assert.Equal(0, await reader.Set<UserIdentity>().CountAsync());
        Assert.Equal(0, await reader.Set<UserRole>().CountAsync());
    }

    /// <summary>
    /// And a rolled-back attempt leaves the tenant provisionable: the retry
    /// must succeed, or "delivery failed, try again" would be a lie.
    /// </summary>
    [Fact]
    public async Task A_retry_after_a_failed_delivery_succeeds()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionPlatformAsync(database);

        await using (var context = database.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Bootstrap(context).ProvisionAsync(
                    Request, Now,
                    (_, _) => throw new InvalidOperationException("nope"),
                    CancellationToken.None));
        }

        await using (var context = database.CreateContext())
        {
            Assert.NotNull(
                await Bootstrap(context).ProvisionAsync(
                    Request, Now, Deliver, CancellationToken.None));
        }
    }

    /// <summary>
    /// The sentinel is "any Human actor exists", so a second run writes nothing
    /// and — importantly — does not deliver a token that would not work.
    /// </summary>
    [Fact]
    public async Task PRV_C3_is_idempotent_and_delivers_nothing_on_a_no_op()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await ProvisionPlatformAsync(database);

        await using (var context = database.CreateContext())
        {
            await Bootstrap(context).ProvisionAsync(
                Request, Now, Deliver, CancellationToken.None);
        }

        var deliveries = 0;

        await using (var context = database.CreateContext())
        {
            var second = await Bootstrap(context).ProvisionAsync(
                Request,
                Now.AddDays(1),
                (_, _) => { deliveries++; return Task.CompletedTask; },
                CancellationToken.None);

            Assert.Null(second);
        }

        Assert.Equal(0, deliveries);

        await using var reader = database.CreateContext();

        Assert.Equal(
            1,
            await reader.Set<User>().CountAsync(x => x.ActorType == ActorType.Human));
    }

    /// <summary>
    /// PRV-C3 depends on roles PRV-C1 seeds, so running it first must fail
    /// loudly rather than produce an administrator with no roles.
    /// </summary>
    [Fact]
    public async Task PRV_C3_before_PRV_C1_is_refused()
    {
        await using var database = await ThrowawayDatabase.CreateAsync();

        await using var context = database.CreateContext();

        var failure = await Assert.ThrowsAsync<ProvisioningException>(
            () => Bootstrap(context).ProvisionAsync(
                Request, Now, Deliver, CancellationToken.None));

        Assert.Contains("System actor", failure.Message);
    }

    // ------------------------------------------------------------ harness

    private static readonly BootstrapAdministratorRequest Request =
        new("Ada", "Lovelace", "Ada Lovelace",
            "ada@example.test", "ada.lovelace");

    private static Task Deliver(
        BootstrapAdministratorResult result, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static BootstrapAdministratorProvisioner Bootstrap(
        LigatureDbContext context)
        => new(
            context,
            new SecurityPolicyResolver(context),
            new UserTokenService());

    private static async Task ProvisionPlatformAsync(ThrowawayDatabase database)
    {
        await using var context = database.CreateContext();

        await new PlatformProvisioner(context)
            .ProvisionAsync(Now, CancellationToken.None);
    }
}
