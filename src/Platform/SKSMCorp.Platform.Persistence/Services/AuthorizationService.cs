using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace SKSMCorp.Platform.Persistence.Services;

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
///
/// It returns which assignment authorised the act, not merely whether one did.
/// The assignment was always computed here and discarded on the final line;
/// Audit needs it, and the only alternative is reconstructing it later against
/// tables that have since changed, which answers a question about the past
/// with today's configuration.
///
/// THE SELECTION DOES NOT CHANGE WHO IS AUTHORISED. Several assignments may
/// legitimately authorise one act — holding two roles that both carry a
/// permission is an ordinary configuration. The predicate is unchanged and the
/// decision is still "does at least one eligible assignment exist"; ordering
/// only decides which of them is REPORTED. Earliest EffectiveFrom, then
/// assignment id: the selection is deterministic FOR A GIVEN AUTHORISATION
/// STATE. It is not a promise that a later call returns the same assignment —
/// the one selected today may be revoked tomorrow. That is precisely why the
/// chosen AssignmentId is recorded on the audit record: historical
/// reconstruction (REV-Q6) is then a direct reference to what authorised the
/// act, not a re-evaluation of what would authorise it now.
/// </summary>
public sealed class AuthorizationService : IAuthorizationService
{
    private readonly SKSMCorpDbContext _dbContext;

    public AuthorizationService(SKSMCorpDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<AuthorizationResult> IsAllowedAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // FIRST, as it was before the shared predicate was extracted.
        // ScopeType.Create throws on a blank value, and that refusal must keep
        // happening before the actor is looked up: moving it later would turn a
        // malformed request into a Denied for an ineligible actor, which is a
        // different answer to a different question.
        var scopeType = ScopeType.Create(request.ScopeType);

        var actorType = await EligibleActorTypeAsync(request.UserId, cancellationToken);

        if (actorType is null)
            return AuthorizationResult.Denied;

        // The scope and the code are the NARROWING, and only this view applies
        // them. Everything else is shared with the enumeration.
        var deciding = await Candidates(
                request.UserId,
                request.At,
                actorType.Value,
                scopeType,
                request.ScopeId,
                request.PermissionCode)
            .FirstOrDefaultAsync(cancellationToken);

        // Same decision as the previous AnyAsync(): none eligible is denial.
        if (deciding is null)
            return AuthorizationResult.Denied;

        return AuthorizationResult.Allowed(
            new AuthorizingAssignment(
                deciding.Assignment.RoleId,
                deciding.Role.Name,
                deciding.Assignment.ScopeType,
                deciding.Assignment.ScopeId,
                deciding.Assignment.Id));
    }

    /// <summary>
    /// Everything this actor effectively holds, at an instant — the read GET
    /// /me needs (B6-B).
    ///
    /// The SAME evaluation as IsAllowedAsync, without its narrowing. Both gate
    /// on EligibleActorTypeAsync and both draw from Candidates; only the single
    /// check adds a scope and a code. That is what stops this becoming a second
    /// authorisation rule that drifts from the first.
    /// </summary>
    public async Task<IReadOnlyList<EffectivePermission>> EnumerateAsync(
        EffectivePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actorType = await EligibleActorTypeAsync(request.UserId, cancellationToken);

        if (actorType is null)
            return [];

        var held = await Candidates(
                request.UserId,
                request.At,
                actorType.Value,
                scopeType: null,
                scopeId: null,
                permissionCode: null)
            .ToListAsync(cancellationToken);

        // Distinct in memory. Holding two roles that both carry a permission is
        // an ordinary configuration, and the caller holds that permission once;
        // EffectivePermission is a record, so equality is structural.
        return held
            .Select(x => new EffectivePermission(
                x.Permission.Code,
                x.Assignment.ScopeType.Value,
                x.Assignment.ScopeId))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// AUT-Q7's view: everyone who could exercise one permission at an instant
    /// (RW2). The same evaluation as the other two, narrowed by code and not
    /// by user.
    /// </summary>
    public Task<IReadOnlyList<PermissionHolder>?> WhoCanDoAsync(
        WhoCanDoRequest request,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-Q7 is not implemented yet.");

    /// <summary>
    /// The actor gates, shared by both views: the user must exist and be
    /// active, and hold at least one active identity. Returns the actor's type,
    /// which UR10 needs, or null when the actor is not eligible at all.
    /// </summary>
    private async Task<ActorType?> EligibleActorTypeAsync(
        UserId userId,
        CancellationToken cancellationToken)
    {
        // Actor state comes from persisted state, never from the caller's own
        // claim about itself. IExecutionContext.ActorType originates at the
        // composition boundary; app_user.ActorType is immutable and is what the
        // assignment-time checks (UR9/RP6) were evaluated against.
        var actor = await _dbContext.Set<User>()
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.ActorType, x.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (actor is null || actor.Status != UserStatus.Active)
            return null;

        // "At least one active identity", per AUT-Q1's parameters, which carry
        // no identity id. Enforcement against the SPECIFIC identity that
        // authenticated belongs to the session layer: IDN-C3 revokes the
        // sessions bound to an identity when it is deactivated.
        var hasActiveIdentity = await _dbContext.Set<UserIdentity>()
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == userId
                    && x.Status == UserStatus.Active,
                cancellationToken);

        return hasActiveIdentity ? actor.ActorType : null;
    }

    /// <summary>
    /// The assignments that could authorise this actor at this instant, before
    /// either view narrows them. SHARED, and deliberately so: the enumeration
    /// and the single check are two views over one evaluation.
    ///
    /// The Role join carries the authorising role's NAME alongside its id,
    /// because renaming a role (AUT-C4) must not rewrite the authority recorded
    /// against acts already performed.
    ///
    /// NOTE WHAT IS ABSENT, here rather than in either caller: role.IsActive is
    /// deliberately NOT part of the predicate. AUT-C5 states that deactivating
    /// a role "prevents NEW assignments" while "existing assignments are
    /// unaffected". Keeping the omission in the shared part is what stops a
    /// later enumeration adding the filter back because it looks safer, and
    /// stranding every holder of a retired role. Regression tests pin it on
    /// both views.
    /// </summary>
    private IQueryable<Candidate> Candidates(
        UserId userId,
        DateTimeOffset at,
        ActorType actorType,
        ScopeType? scopeType,
        Guid? scopeId,
        string? permissionCode)
    {
        // EVERY condition is in this one where clause, and the projection is
        // the last operator. EF cannot translate a filter applied AFTER a
        // projection to a named type, so a caller that narrowed afterwards
        // would throw at run time rather than compile-time — the narrowing
        // therefore arrives as parameters instead.
        //
        // scopeType is the single "no narrowing" signal for the scope: scopeId
        // cannot be, because a Global assignment legitimately has a null id.
        return from assignment in _dbContext.Set<UserRole>().AsNoTracking()
               join grant in _dbContext.Set<RolePermission>().AsNoTracking()
                   on assignment.RoleId equals grant.RoleId
               join permission in _dbContext.Set<Permission>().AsNoTracking()
                   on grant.PermissionId equals permission.Id
               join role in _dbContext.Set<Role>().AsNoTracking()
                   on assignment.RoleId equals role.Id
               where assignment.UserId == userId

                   // Within its effective period at the instant asked about.
                   && assignment.EffectiveFrom <= at
                   && (assignment.EffectiveTo == null
                       || at < assignment.EffectiveTo)

                   // Beyond UR12's literal text. Revocation normally closes
                   // EffectiveTo (AUT-C2), but UR3 only requires EffectiveTo to
                   // be non-null when revoked — not to be in the past. A revoked
                   // row with a future EffectiveTo would otherwise still
                   // authorise.
                   && assignment.RevokedAt == null

                   // Live grants only (RP2). Revoked rows are retained so the
                   // historical meaning of a role stays reconstructable.
                   && grant.RevokedAt == null

                   && permission.IsActive

                   // UR10 — the third edge of the two-edge check. UR9 blocks the
                   // assignment and RP6 blocks the grant; this blocks the act.
                   // Defence in depth: a role that acquired a human-only
                   // permission through some path those two missed still cannot
                   // be exercised by a non-human.
                   && (actorType == ActorType.Human
                       || !permission.RequiresHumanActor)

                   // Exact scope match, when a scope was asked about. V1 is
                   // Global-only; a Global assignment does NOT yet imply
                   // narrower scopes, and that inheritance is a decision for
                   // whoever introduces the first non-Global scope.
                   && (scopeType == null
                       || (assignment.ScopeType == scopeType
                           && assignment.ScopeId == scopeId))

                   && (permissionCode == null || permission.Code == permissionCode)

               orderby assignment.EffectiveFrom, assignment.Id
               select new Candidate(assignment, role, permission);
    }

    private sealed record Candidate(
        UserRole Assignment,
        Role Role,
        Permission Permission);
}
