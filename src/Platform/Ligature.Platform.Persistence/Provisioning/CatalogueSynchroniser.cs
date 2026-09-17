using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Audit;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Provisioning;

/// <summary>
/// PRV-C2 — catalogue synchronisation.
///
/// The permission catalogue, the role catalogue and the role-permission grants
/// a release defines reach every existing database, not only databases
/// provisioned after the change. ProvisionAsync returns as soon as the System
/// actor exists, before it reaches any catalogue seeding, so without this a
/// permission added to the seeds changes nothing for an existing tenant.
///
/// THE GOVERNING PRINCIPLE (docs/requirements.md, PRV-C2):
///
///     Catalogue synchronisation is monotonic with respect to authorization.
///     Authorization expansion is permitted only where explicitly represented
///     by an additive catalogue entry; authorization reduction is never
///     performed by synchronisation.
///
/// Monotonic does not mean "always succeeds". Anything this is not entitled to
/// change is REFUSED, and nothing is applied: a permission in the database but
/// not in the catalogue is not retired, a RequiresHumanActor difference is not
/// repaired, a revoked grant the catalogue still lists is not re-granted.
///
/// DIRECTION IS THE WHOLE DESIGN. Seed to database is additive reconciliation;
/// database to seed is drift and a refusal. The catalogue is authoritative
/// about what a release INTRODUCES, never about what may be TAKEN AWAY.
///
/// It does not modify ProvisionAsync, and reads its seed lists without changing
/// them. Catalogue evolution is removed from the first-provision lifecycle
/// rather than bolted onto it.
/// </summary>
public sealed class CatalogueSynchroniser
{
    private readonly LigatureDbContext _dbContext;
    private readonly string _auditConnectionString;
    private readonly string _releaseIdentifier;

    /// <param name="dbContext">Opened as migration_role.</param>
    /// <param name="auditConnectionString">
    /// The audit record is written on a connection of its own so it survives
    /// the rollback of a refused catalogue transaction. Npgsql permits one
    /// transaction per connection, so a separate connection is what makes
    /// "independently" true rather than approximate.
    /// </param>
    /// <param name="releaseIdentifier">
    /// The synchronisation tool's assembly informational version, recorded in
    /// the audit payload.
    /// </param>
    public CatalogueSynchroniser(
        LigatureDbContext dbContext,
        string auditConnectionString,
        string releaseIdentifier)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseIdentifier);

        _dbContext = dbContext;
        _auditConnectionString = auditConnectionString;
        _releaseIdentifier = releaseIdentifier;
    }

    /// <summary>
    /// Reconcile, or refuse. Throws <see cref="CatalogueAuditNotRecordedException"/>
    /// when the catalogue committed and its audit record did not.
    /// </summary>
    public async Task<CatalogueSyncResult> SynchroniseAsync(
        DateTimeOffset executionTimestamp,
        CancellationToken cancellationToken)
    {
        // FIRST, and before anything else is read. A database with no System
        // actor has no catalogue to reconcile and no actor to attribute an
        // event to; ./up.sh reaches exactly this state, because the provisioner
        // sits behind the bootstrap Compose profile.
        //
        // "No System actor" means the lookup SUCCEEDED and returned none. A
        // lookup that fails is not a bootstrap: it propagates, and the caller
        // reports an operational failure rather than a clean no-op.
        var systemActor = await _dbContext.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == User.SystemUserId, cancellationToken);

        if (systemActor is null)
        {
            return new CatalogueSyncResult(
                CatalogueSyncOutcome.NotProvisioned, CatalogueSyncCounts.None, []);
        }

        var result = await ReconcileAsync(executionTimestamp, cancellationToken);

        // The outcome is settled — committed or rolled back — before the record
        // describing it is written, on a connection of its own.
        try
        {
            await RecordAsync(result, executionTimestamp, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new CatalogueAuditNotRecordedException(result, failure);
        }

        return result;
    }

    /// <summary>
    /// One transaction. Every finding is classified before any mutation is
    /// applied, so a refusal cannot leave half of an additive run behind.
    /// </summary>
    private async Task<CatalogueSyncResult> ReconcileAsync(
        DateTimeOffset executionTimestamp,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var permissions = await _dbContext.Set<Permission>().ToListAsync(cancellationToken);
        var roles = await _dbContext.Set<Role>().ToListAsync(cancellationToken);
        var grants = await _dbContext.Set<RolePermission>().ToListAsync(cancellationToken);

        var permissionSeeds = PlatformProvisioner.GetPermissionSeeds();
        var roleSeeds = PlatformProvisioner.GetRoleSeeds();
        var grantSeeds = PlatformProvisioner.GetRolePermissionSeeds();

        var byPermissionCode = permissions.ToDictionary(x => x.Code, StringComparer.Ordinal);
        var byRoleCode = roles.ToDictionary(x => x.Code, StringComparer.Ordinal);

        var refusals = new List<CatalogueRefusal>();

        // ---------------------------------------------------- database -> seed
        //
        // Drift. The catalogue is authoritative about what a release
        // INTRODUCES, never about what may be TAKEN AWAY.
        var seededPermissionCodes = permissionSeeds.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        var seededRoleCodes = roleSeeds.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        var seededGrants = grantSeeds
            .Select(x => $"{x.RoleCode}/{x.PermissionCode}")
            .ToHashSet(StringComparer.Ordinal);

        refusals.AddRange(permissions
            .Where(x => !seededPermissionCodes.Contains(x.Code))
            .Select(x => new CatalogueRefusal(CatalogueRefusalReason.PermissionMissingFromSeed, x.Code)));

        refusals.AddRange(roles
            .Where(x => !seededRoleCodes.Contains(x.Code))
            .Select(x => new CatalogueRefusal(CatalogueRefusalReason.RoleMissingFromSeed, x.Code)));

        foreach (var grant in grants.Where(x => x.IsActive))
        {
            var subject = SubjectOf(grant, roles, permissions);

            if (subject is not null && !seededGrants.Contains(subject))
                refusals.Add(new(CatalogueRefusalReason.GrantMissingFromSeed, subject));
        }

        // ---------------------------------------------------- seed -> database
        var toInsertPermissions = new List<PermissionSeedPlan>();
        var metadataPermissions = new List<(Permission Row, string Name, string? Description)>();

        foreach (var seed in permissionSeeds)
        {
            if (!byPermissionCode.TryGetValue(seed.Code, out var row))
            {
                toInsertPermissions.Add(new(seed));
                continue;
            }

            if (!row.IsActive)
            {
                refusals.Add(new(CatalogueRefusalReason.InactiveCatalogueEntry, seed.Code));
                continue;
            }

            // F7. Immutable in the domain, so drift here can only be repaired by
            // adding a mutator — which the contract forbids.
            if (!string.Equals(row.Resource, seed.Resource, StringComparison.Ordinal)
                || !string.Equals(row.Action, seed.Action, StringComparison.Ordinal)
                || row.RequiresHumanActor != seed.RequiresHumanActor)
            {
                refusals.Add(new(CatalogueRefusalReason.SecuritySemanticDrift, seed.Code));
                continue;
            }

            if (!string.Equals(row.Name, seed.Name, StringComparison.Ordinal)
                || !string.Equals(row.Description, seed.Description, StringComparison.Ordinal))
            {
                metadataPermissions.Add((row, seed.Name, seed.Description));
            }
        }

        var toInsertRoles = new List<RoleSeedPlan>();
        var metadataRoles = new List<(Role Row, string Name, string? Description)>();

        foreach (var seed in roleSeeds)
        {
            if (!byRoleCode.TryGetValue(seed.Code, out var row))
            {
                toInsertRoles.Add(new(seed));
                continue;
            }

            if (!row.IsActive)
            {
                refusals.Add(new(CatalogueRefusalReason.InactiveCatalogueEntry, seed.Code));
                continue;
            }

            if (!row.IsSystemRole)
            {
                refusals.Add(new(CatalogueRefusalReason.SecuritySemanticDrift, seed.Code));
                continue;
            }

            if (!string.Equals(row.Name, seed.Name, StringComparison.Ordinal)
                || !string.Equals(row.Description, seed.Description, StringComparison.Ordinal))
            {
                metadataRoles.Add((row, seed.Name, seed.Description));
            }
        }

        var toInsertGrants = new List<PlatformProvisioner.RolePermissionSeed>();

        foreach (var seed in grantSeeds)
        {
            var existing = grants.FirstOrDefault(x =>
                SubjectOf(x, roles, permissions) == $"{seed.RoleCode}/{seed.PermissionCode}");

            if (existing is null)
            {
                toInsertGrants.Add(seed);
                continue;
            }

            // Revoked means a human decided. Re-granting is an authorization
            // expansion no additive catalogue entry represents.
            if (!existing.IsActive)
                refusals.Add(new(CatalogueRefusalReason.RevokedGrantInSeed, $"{seed.RoleCode}/{seed.PermissionCode}"));

            // An active grant that exists is NEVER touched (F8).
        }

        if (refusals.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new CatalogueSyncResult(
                CatalogueSyncOutcome.Refused, CatalogueSyncCounts.None, refusals);
        }

        // ------------------------------------------------------------- apply
        //
        // Foreign-key order: permissions, then roles, then the grants that
        // reference both.
        foreach (var plan in toInsertPermissions)
        {
            var seed = plan.Seed;

            var permission = Permission.Create(
                PermissionId.New(), seed.Code, seed.Name, seed.Description,
                seed.Resource, seed.Action, seed.RequiresHumanActor,
                executionTimestamp, User.SystemUserId);

            _dbContext.Add(permission);
            byPermissionCode[seed.Code] = permission;
        }

        foreach (var (row, name, description) in metadataPermissions)
            row.UpdateMetadata(name, description);

        foreach (var plan in toInsertRoles)
        {
            var seed = plan.Seed;

            var role = Role.Create(
                RoleId.New(), name: seed.Name, code: seed.Code, seed.Description,
                isSystemRole: true, executionTimestamp, User.SystemUserId);

            _dbContext.Add(role);
            byRoleCode[seed.Code] = role;
        }

        foreach (var (row, name, description) in metadataRoles)
            row.UpdateMetadata(name, description);

        foreach (var seed in toInsertGrants)
        {
            _dbContext.Add(
                RolePermission.Create(
                    RolePermissionId.New(),
                    byRoleCode[seed.RoleCode].Id,
                    byPermissionCode[seed.PermissionCode].Id,
                    executionTimestamp,
                    User.SystemUserId));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CatalogueSyncResult(
            CatalogueSyncOutcome.Succeeded,
            new CatalogueSyncCounts(
                toInsertPermissions.Count,
                metadataPermissions.Count,
                toInsertRoles.Count,
                metadataRoles.Count,
                toInsertGrants.Count),
            []);
    }

    private static string? SubjectOf(
        RolePermission grant,
        IReadOnlyList<Role> roles,
        IReadOnlyList<Permission> permissions)
    {
        var role = roles.FirstOrDefault(x => x.Id == grant.RoleId);
        var permission = permissions.FirstOrDefault(x => x.Id == grant.PermissionId);

        return role is null || permission is null ? null : $"{role.Code}/{permission.Code}";
    }

    /// <summary>
    /// The record of what happened, written on a connection of its own AFTER
    /// the catalogue transaction has settled — so a refused run, whose
    /// mutations were rolled back, still evidences its refusal.
    ///
    /// It carries counts and reason codes. Never the changed rows, never a
    /// digest of them, and not the database's name: behaviour 14's secret scan
    /// rejects a long hex digest and a high-entropy identifier, and a record
    /// that fails the scan is not written at all.
    /// the database is the source of truth for what the catalogue now
    /// contains, and Audit establishes that reconciliation happened and how it
    /// ended. The readable detail — which permission, which field — is the
    /// operator's, and putting it here would put catalogue contents in the
    /// trail.
    /// </summary>
    private async Task RecordAsync(
        CatalogueSyncResult result,
        DateTimeOffset executionTimestamp,
        CancellationToken cancellationToken)
    {
        var declaration = new AuditEventDeclaration("PermissionCatalogUpdated", version: 1)
            .Primary("PermissionCatalog")
            .WithPayload(new
            {
                release = _releaseIdentifier,
                outcome = result.Outcome.ToString(),
                permissionsInserted = result.Counts.PermissionsInserted,
                permissionMetadataReconciled = result.Counts.PermissionMetadataReconciled,
                rolesInserted = result.Counts.RolesInserted,
                roleMetadataReconciled = result.Counts.RoleMetadataReconciled,
                grantsInserted = result.Counts.GrantsInserted,
                driftDetected = result.Refusals.Count > 0,
                refusalReasons = result.RefusalsByReason
                    .Select(x => new { reason = x.Reason.ToString(), count = x.Count })
                    .ToArray(),
            });

        var rows = AuditRecordAssembler.Assemble(
            [declaration],
            AuditDeclarations.For(typeof(CatalogueSynchronisation))!,
            ActorSnapshot.System(executionTimestamp),
            AuditEventCatalogueLoader.Load(_auditConnectionString),
            AuditWritePath.Autonomous,
            operationId: Guid.CreateVersion7(),
            occurredAt: executionTimestamp,
            capturedAt: executionTimestamp);

        await new AutonomousAuditRecordWriter(_auditConnectionString)
            .WriteAsync(rows, cancellationToken);
    }

    private sealed record PermissionSeedPlan(PlatformProvisioner.PermissionSeed Seed);

    private sealed record RoleSeedPlan(PlatformProvisioner.RoleSeed Seed);
}
