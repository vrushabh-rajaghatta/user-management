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
public sealed class SystemActorProvisioner
{
    private readonly LigatureDbContext _dbContext;

    public SystemActorProvisioner(LigatureDbContext dbContext)
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
