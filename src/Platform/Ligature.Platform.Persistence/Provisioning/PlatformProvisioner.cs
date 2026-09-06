using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Provisioning;

/// <summary>
/// Establishes the System actor that roots every provenance chain, plus the
/// baseline platform state seeded alongside it.
/// The System actor is the provisioning sentinel: if it already exists, the
/// entire operation is a no-op, so a partially provisioned database is never
/// mistaken for a successfully provisioned one.
/// </summary>
public sealed class PlatformProvisioner
{
    private readonly LigatureDbContext _dbContext;

    public PlatformProvisioner(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<User> ProvisionAsync(
        DateTimeOffset executionTimestamp,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var occupant = await _dbContext.Set<User>()
            .FindAsync([User.SystemUserId], cancellationToken);

        var systemActor = await _dbContext.Set<User>()
            .FirstOrDefaultAsync(
                x => x.ActorType == ActorType.System,
                cancellationToken);

        if (occupant is not null && occupant.ActorType != ActorType.System)
        {
            throw new ProvisioningException(
                $"The System actor id {User.SystemUserId} is already held by a "
                + $"'{occupant.ActorType}' actor. Provisioning cannot continue.");
        }

        if (systemActor is not null && systemActor.Id != User.SystemUserId)
        {
            throw new ProvisioningException(
                $"A System actor already exists under id {systemActor.Id}, which "
                + $"is not the expected id {User.SystemUserId}. Provisioning "
                + "cannot continue.");
        }

        if (occupant is not null)
        {
            ValidateStructure(occupant);

            await transaction.CommitAsync(cancellationToken);

            return occupant;
        }

        var created = User.CreateSystem(executionTimestamp);

        var entry = _dbContext.Add(created);

        // No interceptor stamps these yet, and both columns are NOT NULL.
        entry.Property<DateTimeOffset>("UpdatedAt").CurrentValue = executionTimestamp;
        entry.Property<UserId>("UpdatedBy").CurrentValue = User.SystemUserId;

        foreach (var seed in GetPermissionSeeds())
        {
            _dbContext.Add(
                Permission.Create(
                    PermissionId.New(),
                    seed.Code,
                    seed.Name,
                    seed.Description,
                    seed.Resource,
                    seed.Action,
                    seed.RequiresHumanActor,
                    executionTimestamp,
                    User.SystemUserId));
        }

        // security_policy declares no UpdatedAt/UpdatedBy shadow properties,
        // so nothing to stamp here.
        _dbContext.Add(CreateInitialSecurityPolicy(executionTimestamp));

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return created;
    }

    private static SecurityPolicy CreateInitialSecurityPolicy(
        DateTimeOffset executionTimestamp)
    {
        var settings = new SecurityPolicySettings(
            PasswordMinLength: 12,
            PasswordHistoryDepth: 10,
            LockoutDuration: TimeSpan.FromMinutes(15),
            MaxFailedLoginAttempts: 5,
            ActivationTokenLifetime: TimeSpan.FromHours(72),
            PasswordResetTokenLifetime: TimeSpan.FromHours(1),
            SessionIdleTimeout: TimeSpan.FromMinutes(15),
            SessionAbsoluteTimeout: TimeSpan.FromHours(12));

        // Constructed explicitly rather than via DateTimeOffset.MinValue: the
        // baseline policy is effective from 0001-01-01T00:00:00Z by decision,
        // not because that is the framework's minimum representable value.
        // Npgsql persists this as the PostgreSQL sentinel -infinity, which
        // round-trips losslessly and orders before every real timestamp.
        var effectiveFrom = new DateTimeOffset(
            1, 1, 1,
            0, 0, 0,
            TimeSpan.Zero);

        return SecurityPolicy.Create(
            SecurityPolicyId.New(),
            policyVersion: 1,
            effectiveFrom,
            settings,
            executionTimestamp,
            User.SystemUserId);
    }

    private static IReadOnlyList<PermissionSeed> GetPermissionSeeds()
    {
        return
        [
            new(
                "user.create",
                "Create User",
                "User",
                "Create",
                true,
                "Create a new human user account and issue its activation token."),

            new(
                "user.read",
                "View Users",
                "User",
                "Read",
                false,
                "View user accounts and their current lifecycle status."),

            new(
                "user.update",
                "Update User Profile",
                "User",
                "Update",
                false,
                "Change a user's first name, last name or display name."),

            new(
                "user.deactivate",
                "Deactivate User",
                "User",
                "Deactivate",
                true,
                "Deactivate a user, revoking their sessions and role assignments."),

            new(
                "user.reactivate",
                "Reactivate User",
                "User",
                "Reactivate",
                true,
                "Return a deactivated user to active status. Grants no previous access."),

            new(
                "user.resetpassword",
                "Reset User Password",
                "User",
                "ResetPassword",
                false,
                "Issue a password reset token to a user's registered email address."),

            new(
                "user.unlock",
                "Unlock User Account",
                "User",
                "Unlock",
                false,
                "Clear a lockout arising from consecutive failed sign-in attempts."),

            new(
                "identity.read",
                "View Identities",
                "Identity",
                "Read",
                false,
                "View the authentication identities attached to a user."),

            new(
                "identity.manage",
                "Manage Identities",
                "Identity",
                "Manage",
                true,
                "Attach, deactivate or reactivate a user's authentication identities."),

            new(
                "session.read",
                "View Sessions",
                "Session",
                "Read",
                false,
                "View active and historical sign-in sessions."),

            new(
                "session.revoke",
                "Revoke Sessions",
                "Session",
                "Revoke",
                false,
                "Terminate another user's active sessions."),

            new(
                "role.read",
                "View Roles",
                "Role",
                "Read",
                false,
                "View role definitions and the permissions they confer."),

            new(
                "role.manage",
                "Manage Role Definitions",
                "Role",
                "Manage",
                false,
                "Create and amend role definitions and their permission grants."),

            new(
                "role.grant",
                "Grant Role",
                "Role",
                "Grant",
                true,
                "Assign a role to a user over a given scope and period."),

            new(
                "role.revoke",
                "Revoke Role",
                "Role",
                "Revoke",
                true,
                "End a user's role assignment before its effective period expires."),

            new(
                "securitypolicy.read",
                "View Security Policy",
                "SecurityPolicy",
                "Read",
                false,
                "View the effective security policy and its version history."),

            new(
                "securitypolicy.change",
                "Change Security Policy",
                "SecurityPolicy",
                "Change",
                true,
                "Create a new security policy version for the tenant."),

            new(
                "accessreview.read",
                "Run Access Review",
                "AccessReview",
                "Read",
                false,
                "Produce access review reports of who holds which roles."),

            new(
                "agent.manage",
                "Administer Agents",
                "Agent",
                "Manage",
                true,
                "Administer software agents and their accountable ownership.")
        ];
    }

    private sealed record PermissionSeed(
        string Code,
        string Name,
        string Resource,
        string Action,
        bool RequiresHumanActor,
        string Description);

    private static void ValidateStructure(User existing)
    {
        var mismatches = new List<string>();

        if (existing.DisplayName != User.SystemDisplayName)
        {
            mismatches.Add(
                $"display name is '{existing.DisplayName}', expected "
                + $"'{User.SystemDisplayName}'");
        }

        if (existing.FirstName is not null)
            mismatches.Add("first name is not null");

        if (existing.LastName is not null)
            mismatches.Add("last name is not null");

        if (existing.Email is not null)
            mismatches.Add("email is not null");

        if (existing.Status != UserStatus.Active)
            mismatches.Add($"status is '{existing.Status}', expected 'Active'");

        if (existing.CreatedBy != User.SystemUserId)
        {
            mismatches.Add(
                $"created by is {existing.CreatedBy}, expected "
                + $"{User.SystemUserId}");
        }

        if (existing.Deactivation is not null)
            mismatches.Add("deactivation stamp is not null");

        if (mismatches.Count > 0)
        {
            throw new ProvisioningException(
                "The existing System actor does not match the structure "
                + $"defined by {nameof(User)}.{nameof(User.CreateSystem)}: "
                + string.Join("; ", mismatches)
                + ".");
        }
    }
}
