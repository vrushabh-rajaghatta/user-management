using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Provisioning;

public sealed record BootstrapAdministratorRequest(
    string FirstName,
    string LastName,
    string DisplayName,
    string Email,
    string Username);

/// <summary>
/// The plaintext activation token is returned here for delivery and is never
/// persisted; only its hash reaches the database.
/// </summary>
public sealed record BootstrapAdministratorResult(
    UserId UserId,
    UserIdentityId IdentityId,
    string ActivationToken,
    DateTimeOffset ActivationTokenExpiresAt);

/// <summary>
/// PRV-C3. Creates the first human administrator directly, because USR-C1
/// requires a caller holding user.create and no such caller exists yet.
/// Idempotency key is "any Human actor exists", which is deliberately distinct
/// from PRV-C1's System-actor sentinel.
/// </summary>
public sealed class BootstrapAdministratorProvisioner
{
    private const string UserAdministratorCode = "user-administrator";
    private const string SecurityAdministratorCode = "security-administrator";
    private const string AssignmentReason = "Initial tenant provisioning";

    private readonly LigatureDbContext _dbContext;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IUserTokenService _userTokenService;

    public BootstrapAdministratorProvisioner(
        LigatureDbContext dbContext,
        ISecurityPolicyResolver securityPolicyResolver,
        IUserTokenService userTokenService)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);
        ArgumentNullException.ThrowIfNull(userTokenService);

        _dbContext = dbContext;
        _securityPolicyResolver = securityPolicyResolver;
        _userTokenService = userTokenService;
    }

    /// <returns>
    /// The created administrator, or <c>null</c> when a human actor already
    /// exists and nothing was written.
    /// </returns>
    public async Task<BootstrapAdministratorResult?> ProvisionAsync(
        BootstrapAdministratorRequest request,
        DateTimeOffset executionTimestamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var systemActor = await _dbContext.Set<User>()
            .FindAsync([User.SystemUserId], cancellationToken);

        if (systemActor is null || systemActor.ActorType != ActorType.System)
        {
            throw new ProvisioningException(
                "The System actor does not exist, so PRV-C1 has not run. The "
                + "bootstrap administrator cannot be provisioned.");
        }

        var humanExists = await _dbContext.Set<User>()
            .AnyAsync(x => x.ActorType == ActorType.Human, cancellationToken);

        if (humanExists)
        {
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        var rolesByCode = await LoadAdministratorRolesAsync(cancellationToken);

        var settings = await _securityPolicyResolver
            .GetEffectiveSettingsAsync(executionTimestamp, cancellationToken);

        // The id is minted first because the delivered token embeds it:
        // CRD-C1 consumes by primary key.
        var tokenId = UserTokenId.New();

        var tokenMaterial = _userTokenService.Generate(tokenId);

        var userId = UserId.New();
        var identityId = UserIdentityId.New();

        var administrator = User.CreateHuman(
            userId,
            request.FirstName,
            request.LastName,
            request.DisplayName,
            request.Email,
            executionTimestamp,
            User.SystemUserId);

        // UpdatedAt/UpdatedBy are stamped by ProvenanceStampingInterceptor.
        _dbContext.Add(administrator);

        _dbContext.Add(
            UserIdentity.CreateLocal(
                identityId,
                userId,
                ActorType.Human,
                request.Username,
                executionTimestamp,
                User.SystemUserId));

        var expiresAt = executionTimestamp + settings.ActivationTokenLifetime;

        // Only the hash is persisted. The administrator proves control of the
        // mailbox and sets their own password; provisioning must never know it,
        // or every approval they make afterwards is contestable.
        _dbContext.Add(
            UserToken.Create(
                tokenId,
                identityId,
                TokenType.Activation,
                tokenMaterial.Hash,
                executionTimestamp,
                expiresAt,
                User.SystemUserId));

        foreach (var roleCode in new[] { UserAdministratorCode, SecurityAdministratorCode })
        {
            _dbContext.Add(
                UserRole.Create(
                    UserRoleId.New(),
                    userId,
                    ActorType.Human,
                    rolesByCode[roleCode],
                    ScopeType.Global,
                    scopeId: null,
                    effectiveFrom: executionTimestamp,
                    effectiveTo: null,
                    assignedAt: executionTimestamp,
                    assignedBy: User.SystemUserId,
                    assignmentReason: AssignmentReason,
                    createdAt: executionTimestamp,
                    createdBy: User.SystemUserId));
        }

        // No Credential row is created here, by design.

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new BootstrapAdministratorResult(
            userId,
            identityId,
            tokenMaterial.PlainText,
            expiresAt);
    }

    private async Task<Dictionary<string, RoleId>> LoadAdministratorRolesAsync(
        CancellationToken cancellationToken)
    {
        // Role ids are generated during PRV-C1, so they are resolved by code.
        var found = await _dbContext.Set<Role>()
            .Where(x => x.Code == UserAdministratorCode
                || x.Code == SecurityAdministratorCode)
            .ToListAsync(cancellationToken);

        var rolesByCode = found.ToDictionary(
            x => x.Code,
            x => x.Id,
            StringComparer.Ordinal);

        var missing = new[] { UserAdministratorCode, SecurityAdministratorCode }
            .Where(code => !rolesByCode.ContainsKey(code))
            .ToList();

        if (missing.Count > 0)
        {
            throw new ProvisioningException(
                "The system roles required by the bootstrap administrator are "
                + $"missing: {string.Join(", ", missing)}. PRV-C1 seeds them.");
        }

        return rolesByCode;
    }
}
