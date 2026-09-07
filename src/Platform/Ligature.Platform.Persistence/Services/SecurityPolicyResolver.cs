using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// Resolves the security policy actually enforced at a given instant (POL-Q1).
///
/// Two layers combine. The tenant's own configuration is the newest
/// security_policy version whose EffectiveFrom has passed — resolution is
/// deterministic because EffectiveFrom is unique (SP1). The release baseline is
/// then applied per setting, per that setting's declared direction (SP3/SP4).
///
/// Resolution happens at EVALUATION time, never at write time, and that is the
/// point of inv. 31. A release that tightens the baseline is enforced the
/// moment it deploys: a tenant whose stored value was valid when written but is
/// no longer compliant gets the stricter value immediately, rather than
/// continuing to run a policy the product's documentation calls unacceptable
/// until somebody notices.
///
/// Consequently this type NEVER writes. The stored tenant policy stays exactly
/// as configured; only the computed answer differs.
/// </summary>
public sealed class SecurityPolicyResolver : ISecurityPolicyResolver
{
    private readonly LigatureDbContext _dbContext;

    public SecurityPolicyResolver(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<SecurityPolicySettings> GetEffectiveSettingsAsync(
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var tenantPolicy = await _dbContext.Set<SecurityPolicy>()
            .AsNoTracking()
            .Where(x => x.EffectiveFrom <= at)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        // Provisioning seeds version 1 at tenant creation, so an effective
        // policy always exists (SP2). Absence means the database was never
        // provisioned — a broken tenant, not a case to paper over with
        // defaults, which would silently enforce something nobody configured.
        if (tenantPolicy is null)
        {
            throw new ProvisioningException(
                $"No security policy is effective at {at:O}. PRV-C1 seeds "
                + "version 1 at tenant creation, so this database has not been "
                + "provisioned.");
        }

        return tenantPolicy.Settings.EffectiveAgainst(SecurityBaseline.Current);
    }
}
