using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// AUT-Q1 — the authorisation resolver, narrowed to the single question the
/// pipeline asks: may this actor exercise this permission, in this scope, at
/// this instant?
///
/// Invariant 7: "No single record authorises alone." The full predicate is
/// user active + identity active + assignment in effect + scope match +
/// permission active, and the spec is blunt about why the omission matters:
/// "an active assignment belonging to a deactivated user must grant nothing —
/// the omission of that condition is exactly the defect that allows a departed
/// employee to continue acting."
///
/// NOTE WHAT IS ABSENT: role.IsActive is deliberately NOT part of the
/// predicate. AUT-C5 states that deactivating a role "prevents NEW assignments"
/// while "existing assignments are unaffected". Adding the filter because it
/// looks safer would silently strip access from every current holder of a
/// retired role. A regression test pins this.
/// </summary>
public sealed class AuthorizationService : IAuthorizationService
{
    private readonly LigatureDbContext _dbContext;

    public AuthorizationService(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<bool> IsAllowedAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scopeType = ScopeType.Create(request.ScopeType);

        // Actor state comes from persisted state, never from the caller's own
        // claim about itself. IExecutionContext.ActorType originates at the
        // composition boundary; app_user.ActorType is immutable and is what the
        // assignment-time checks (UR9/RP6) were evaluated against.
        var actor = await _dbContext.Set<User>()
            .AsNoTracking()
            .Where(x => x.Id == request.UserId)
            .Select(x => new { x.ActorType, x.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (actor is null || actor.Status != UserStatus.Active)
            return false;

        // "At least one active identity", per AUT-Q1's parameters, which carry
        // no identity id. Enforcement against the SPECIFIC identity that
        // authenticated belongs to the session layer: IDN-C3 revokes the
        // sessions bound to an identity when it is deactivated.
        var hasActiveIdentity = await _dbContext.Set<UserIdentity>()
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == request.UserId
                    && x.Status == UserStatus.Active,
                cancellationToken);

        if (!hasActiveIdentity)
            return false;

        var candidates =
            from assignment in _dbContext.Set<UserRole>().AsNoTracking()
            join grant in _dbContext.Set<RolePermission>().AsNoTracking()
                on assignment.RoleId equals grant.RoleId
            join permission in _dbContext.Set<Permission>().AsNoTracking()
                on grant.PermissionId equals permission.Id
            where assignment.UserId == request.UserId

                // Exact scope match. V1 is Global-only; a Global assignment
                // does NOT yet imply narrower scopes, and that inheritance is a
                // decision for whoever introduces the first non-Global scope.
                && assignment.ScopeType == scopeType
                && assignment.ScopeId == request.ScopeId

                // Within its effective period at the instant asked about.
                && assignment.EffectiveFrom <= request.At
                && (assignment.EffectiveTo == null
                    || request.At < assignment.EffectiveTo)

                // Beyond UR12's literal text. Revocation normally closes
                // EffectiveTo (AUT-C2), but UR3 only requires EffectiveTo to be
                // non-null when revoked — not to be in the past. A revoked row
                // with a future EffectiveTo would otherwise still authorise.
                && assignment.RevokedAt == null

                // Live grants only (RP2). Revoked rows are retained so the
                // historical meaning of a role stays reconstructable.
                && grant.RevokedAt == null

                && permission.Code == request.PermissionCode
                && permission.IsActive
            select permission;

        // UR10 — the third edge of the two-edge check. UR9 blocks the
        // assignment and RP6 blocks the grant; this blocks the act. Defence in
        // depth: a role that acquired a human-only permission through some path
        // those two missed still cannot be exercised by a non-human.
        if (actor.ActorType != ActorType.Human)
            candidates = candidates.Where(x => !x.RequiresHumanActor);

        return await candidates.AnyAsync(cancellationToken);
    }
}
