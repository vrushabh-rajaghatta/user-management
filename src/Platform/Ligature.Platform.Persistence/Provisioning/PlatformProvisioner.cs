using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Persistence.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

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

        // UpdatedAt/UpdatedBy are stamped by ProvenanceStampingInterceptor,
        // which attributes them to the System actor when no execution context
        // exists — which is exactly the provisioning case.
        _dbContext.Add(created);

        // Flushed now, still inside the transaction, because AUD-C4 follows
        // and its retention row references this actor (RT5). AUD-S10's order
        // is: schema, System actor, AUD-C4, the remaining UM seeds, then
        // TenantProvisioned as the first record. Two saves in one transaction
        // is what that order costs; a rollback still takes everything.
        await _dbContext.SaveChangesAsync(cancellationToken);

        await ProvisionAuditAsync(transaction, executionTimestamp, cancellationToken);

        var permissionsByCode = new Dictionary<string, PermissionId>(
            StringComparer.Ordinal);

        foreach (var seed in GetPermissionSeeds())
        {
            var permission = Permission.Create(
                PermissionId.New(),
                seed.Code,
                seed.Name,
                seed.Description,
                seed.Resource,
                seed.Action,
                seed.RequiresHumanActor,
                executionTimestamp,
                User.SystemUserId);

            _dbContext.Add(permission);

            permissionsByCode.Add(seed.Code, permission.Id);
        }

        var rolesByCode = new Dictionary<string, RoleId>(
            StringComparer.Ordinal);

        foreach (var seed in GetRoleSeeds())
        {
            // Role.Create takes name before code, and sets IsActive itself.
            var role = Role.Create(
                RoleId.New(),
                name: seed.Name,
                code: seed.Code,
                seed.Description,
                isSystemRole: true,
                executionTimestamp,
                User.SystemUserId);

            _dbContext.Add(role);

            rolesByCode.Add(seed.Code, role.Id);
        }

        // Codes resolve against the entities created above: nothing is in the
        // database until the single SaveChangesAsync below.
        foreach (var seed in GetRolePermissionSeeds())
        {
            _dbContext.Add(
                RolePermission.Create(
                    RolePermissionId.New(),
                    rolesByCode[seed.RoleCode],
                    permissionsByCode[seed.PermissionCode],
                    executionTimestamp,
                    User.SystemUserId));
        }

        // security_policy declares no UpdatedAt/UpdatedBy shadow properties,
        // so nothing to stamp here.
        var initialPolicy = CreateInitialSecurityPolicy(executionTimestamp);

        _dbContext.Add(initialPolicy);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AUD-S10 step 5 — the tenant's first record, after every seed and
        // before the handover probes that will insist it is Sequence 1.
        await EmitTenantProvisionedAsync(
            transaction, executionTimestamp, initialPolicy, cancellationToken);

        // AUD-S11 — the handover probes, last, so they see everything this
        // transaction will commit. A failure rolls all of it back: a tenant is
        // never handed over with a trail that is not in the state its first
        // record expects.
        await AuditHandoverVerification.VerifyHandoverAsync(
            AuditConnection(),
            AuditTransaction(transaction),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return created;
    }

    /// <summary>
    /// AUD-C4 — ProvisionAudit, invoked from PRV-C1 inside its transaction.
    /// Verifies the structure and privileges the seed relies on, then seeds
    /// the event catalogue and retention policy v1. Emits nothing: the
    /// catalogue is being established, not changed, and TenantProvisioned
    /// records its version in its payload.
    ///
    /// Raw SQL on this context's own connection and transaction, because the
    /// audit tables are not this application's to model (docs/architecture.md
    /// section 19): no DbSet, no entity. It is the mechanism the emission
    /// writer will use, established here first.
    /// </summary>
    private async Task ProvisionAuditAsync(
        IDbContextTransaction transaction,
        DateTimeOffset executionTimestamp,
        CancellationToken cancellationToken)
    {
        var connection = AuditConnection();
        var npgsqlTransaction = AuditTransaction(transaction);

        await AuditHandoverVerification.VerifyStructureAsync(
            connection, npgsqlTransaction, cancellationToken);

        await new AuditCatalogueSeeder(connection, npgsqlTransaction)
            .SeedAsync(executionTimestamp, User.SystemUserId, cancellationToken);
    }

    /// <summary>
    /// TenantProvisioned — the one emission that is not a command. PRV-C1 is
    /// a provisioning procedure running in the tool on its own transaction,
    /// so it uses the writer directly rather than through the pipeline;
    /// everything else about the record is held to the same rules, through
    /// the same assembler, against the release seed it has just written
    /// (no process could have loaded a catalogue that does not yet exist).
    ///
    /// Actor SYSTEM_UUID, origin System (AUD-D24); primary Tenant with no
    /// id; a reference to the security policy version it seeded; a payload
    /// recording the catalogue and retention versions, so the first record
    /// states what the tenant was established with (AUD-S05, AUD-S08).
    /// </summary>
    private async Task EmitTenantProvisionedAsync(
        IDbContextTransaction transaction,
        DateTimeOffset executionTimestamp,
        SecurityPolicy initialPolicy,
        CancellationToken cancellationToken)
    {
        var declaration = new AuditEventDeclaration("TenantProvisioned", version: 1)
            .Primary("Tenant")
            .Ref("SecurityPolicy", initialPolicy.Id.Value, role: "InitialVersion")
            .WithPayload(new
            {
                auditCatalogueVersion = AuditEventCatalogue.Version,
                auditRetentionPolicyVersion = 1,
                securityPolicyVersion = 1,
                seededRoleCodes = GetRoleSeeds().Select(x => x.Code).ToArray(),
                permissionCount = GetPermissionSeeds().Count,
            });

        var rows = AuditRecordAssembler.Assemble(
            [declaration],
            AuditDeclarations.For(typeof(PlatformProvisioning))!,
            ActorSnapshot.System(executionTimestamp),
            AuditEventCatalogueLoader.FromSeeds(),
            operationId: Guid.CreateVersion7(),
            occurredAt: executionTimestamp,
            capturedAt: executionTimestamp);

        await AuditRecordWriter.WriteAsync(
            AuditConnection(), AuditTransaction(transaction), rows, cancellationToken);
    }

    private NpgsqlConnection AuditConnection()
        => (NpgsqlConnection)_dbContext.Database.GetDbConnection();

    private static NpgsqlTransaction AuditTransaction(IDbContextTransaction transaction)
        => (NpgsqlTransaction)transaction.GetDbTransaction();

    /// <summary>
    /// Seeded FROM the baseline rather than duplicating its eight values, so a
    /// new tenant starts exactly at the standard it will subsequently be
    /// evaluated against.
    ///
    /// Exposed for the drift test. An already-provisioned database cannot
    /// detect this drifting, because its policy row was written before any such
    /// change — so the linkage has to be asserted on the code, not the data.
    /// </summary>
    internal static SecurityPolicySettings GetInitialSecurityPolicySeed()
        => SecurityBaseline.Current;

    private static SecurityPolicy CreateInitialSecurityPolicy(
        DateTimeOffset executionTimestamp)
    {
        var settings = GetInitialSecurityPolicySeed();

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

    internal static IReadOnlyList<PermissionSeed> GetPermissionSeeds()
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
                true,
                "Issue a password reset token to a user's registered email address."),

            new(
                "user.unlock",
                "Unlock User Account",
                "User",
                "Unlock",
                true,
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
                false,
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
                true,
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

    internal sealed record PermissionSeed(
        string Code,
        string Name,
        string Resource,
        string Action,
        bool RequiresHumanActor,
        string Description);

    internal static IReadOnlyList<RoleSeed> GetRoleSeeds()
    {
        return
        [
            new(
                "user-administrator",
                "User Administrator",
                "Manages the account lifecycle: creating, updating, deactivating and reactivating users, administering their authentication identities, and resolving lockouts."),

            new(
                "security-administrator",
                "Security Administrator",
                "Defines what roles mean and who holds them, and maintains the tenant's security policy."),

            new(
                "access-reviewer",
                "Access Reviewer",
                "Read-only visibility across users, roles, identities, sessions and policy, for periodic access review.")
        ];
    }

    internal sealed record RoleSeed(
        string Code,
        string Name,
        string Description);

    internal static IReadOnlyList<RolePermissionSeed> GetRolePermissionSeeds()
    {
        return
        [
            // user-administrator
            new("user-administrator", "user.create"),
            new("user-administrator", "user.read"),
            new("user-administrator", "user.update"),
            new("user-administrator", "user.deactivate"),
            new("user-administrator", "user.reactivate"),
            new("user-administrator", "user.resetpassword"),
            new("user-administrator", "user.unlock"),
            new("user-administrator", "identity.read"),
            new("user-administrator", "identity.manage"),
            new("user-administrator", "session.read"),
            new("user-administrator", "session.revoke"),

            // security-administrator
            new("security-administrator", "role.read"),
            new("security-administrator", "role.manage"),
            new("security-administrator", "role.grant"),
            new("security-administrator", "role.revoke"),
            new("security-administrator", "securitypolicy.read"),
            new("security-administrator", "securitypolicy.change"),

            // access-reviewer
            new("access-reviewer", "accessreview.read"),
            new("access-reviewer", "user.read"),
            new("access-reviewer", "role.read"),
            new("access-reviewer", "identity.read"),
            new("access-reviewer", "session.read"),
            new("access-reviewer", "securitypolicy.read")
        ];
    }

    internal sealed record RolePermissionSeed(
        string RoleCode,
        string PermissionCode);

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
