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
    private const string Collation = "unicode";

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

        // The scope and the code are the NARROWING, and only this view applies
        // them. Everything else is shared with the other two views — including
        // the actor gates, which are now part of the predicate itself (RW2).
        var deciding = await Candidates(
                request.UserId,
                request.At,
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

        var held = await Candidates(
                request.UserId,
                request.At,
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
    public async Task<IReadOnlyList<PermissionHolder>?> WhoCanDoAsync(
        WhoCanDoRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // FIRST, for the same reason the single check does it first: a blank
        // scope is a malformed request, not an answer about nobody.
        var scopeType = ScopeType.Create(request.ScopeType);

        // Existence is the CATALOGUE's question, not the evaluation's, and the
        // two answers differ: an unknown code is null, a known one that nobody
        // holds is empty, and a retired one is empty because the predicate
        // requires an active permission (RW8, RW9).
        var exists = await _dbContext.Set<Permission>()
            .AsNoTracking()
            .AnyAsync(x => x.Code == request.PermissionCode, cancellationToken);

        if (!exists)
            return null;

        // The ordering is applied HERE rather than in the shared predicate,
        // whose own ordering decides which assignment IsAllowedAsync REPORTS
        // and must not be disturbed. Ordering in SQL is what lets the grouping
        // below be a plain in-memory pass: LINQ's GroupBy preserves encounter
        // order, so the ICU collation still decides the result's order.
        var rows = await Candidates(
                userId: null,
                request.At,
                scopeType,
                request.ScopeId,
                request.PermissionCode,
                CandidateOrder.Holder)
            .Select(x => new
            {
                x.Holder.Id,
                x.Holder.DisplayName,
                x.Holder.Email,
                x.Holder.Status,
                RoleId = x.Role.Id,
                RoleName = x.Role.Name,
            })
            .ToListAsync(cancellationToken);

        // ONE ROW PER USER (RW5), with every authorising role named. A holder
        // reached through three roles is one row naming three, not three rows:
        // the question is "who can do this, and why?", and the person is the
        // who.
        return rows
            .GroupBy(x => x.Id)
            .Select(group => new PermissionHolder(
                group.Key,
                group.First().DisplayName,
                group.First().Email?.Value,
                group.First().Status,
                group
                    .Select(x => new HolderRole(x.RoleId, x.RoleName))
                    .DistinctBy(x => x.RoleId)
                    .ToList()))
            .ToList();
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
        UserId? userId,
        DateTimeOffset at,
        ScopeType? scopeType,
        Guid? scopeId,
        string? permissionCode,
        CandidateOrder order = CandidateOrder.Reporting)
    {
        var identities = _dbContext.Set<UserIdentity>().AsNoTracking();

        // EVERY condition is in this one where clause, and the projection is
        // the last operator. EF cannot translate a filter applied AFTER a
        // projection to a named type, so a caller that narrowed afterwards
        // would throw at run time rather than compile-time — the narrowing
        // therefore arrives as parameters instead.
        //
        // scopeType is the single "no narrowing" signal for the scope: scopeId
        // cannot be, because a Global assignment legitimately has a null id.
        var rows = from assignment in _dbContext.Set<UserRole>().AsNoTracking()
               join grant in _dbContext.Set<RolePermission>().AsNoTracking()
                   on assignment.RoleId equals grant.RoleId
               join permission in _dbContext.Set<Permission>().AsNoTracking()
                   on grant.PermissionId equals permission.Id
               join role in _dbContext.Set<Role>().AsNoTracking()
                   on assignment.RoleId equals role.Id
               join holder in _dbContext.Set<User>().AsNoTracking()
                   on assignment.UserId equals holder.Id

                   // Null means "every user" — the third view's narrowing, and
                   // the same convention scope and code already use.
               where (userId == null || assignment.UserId == userId)

                   // THE ACTOR GATES, here rather than resolved per caller
                   // beforehand (RW2). Invariant 7: an active assignment
                   // belonging to a deactivated user must grant nothing.
                   //
                   // These are CURRENT state and are not reached by `at`
                   // (RW4): status is tied to deactivated_at by a CHECK that
                   // nulls the timestamp again on reactivation, so the schema
                   // retains no status history to resolve against. The
                   // contract says so rather than letting today's state pass
                   // as history.
                   && holder.Status == UserStatus.Active
                   && identities.Any(x => x.UserId == holder.Id
                       && x.Status == UserStatus.Active)

                   // Within its effective period at the instant asked about.
                   && assignment.EffectiveFrom <= at
                   && (assignment.EffectiveTo == null
                       || at < assignment.EffectiveTo)

                   // Beyond UR12's literal text. Revocation normally closes
                   // EffectiveTo (AUT-C2), but UR3 only requires EffectiveTo to
                   // be non-null when revoked — not to be in the past. A revoked
                   // row with a future EffectiveTo would otherwise still
                   // authorise.
                   //
                   // AT THE INSTANT, not merely "ever" (RW3). revoked_at is a
                   // timestamp, so the model DOES represent this temporally,
                   // and answering with today's revocation would tell an
                   // inspection question that nobody ever held the role.
                   // Unchanged for every existing caller, which passes now.
                   && (assignment.RevokedAt == null
                       || at < assignment.RevokedAt)

                   // The grant's own lifecycle, also at the instant (RW3). The
                   // GrantedAt half was absent altogether, so a permission
                   // granted AFTER the instant counted towards authority at
                   // it. This is AUT-Q3's definition of a live grant (RA5),
                   // and there is now one definition rather than two.
                   && grant.GrantedAt <= at
                   && (grant.RevokedAt == null
                       || at < grant.RevokedAt)

                   // CURRENT catalogue state, like the actor gates above and
                   // for the same reason: is_active carries no deactivation
                   // instant, so there is no history to resolve (RW9). A
                   // retired permission authorises nobody, and AUT-Q7 does not
                   // resurrect one.
                   && permission.IsActive

                   // UR10 — the third edge of the two-edge check. UR9 blocks the
                   // assignment and RP6 blocks the grant; this blocks the act.
                   // Defence in depth: a role that acquired a human-only
                   // permission through some path those two missed still cannot
                   // be exercised by a non-human.
                   && (holder.ActorType == ActorType.Human
                       || !permission.RequiresHumanActor)

                   // Exact scope match, when a scope was asked about. V1 is
                   // Global-only; a Global assignment does NOT yet imply
                   // narrower scopes, and that inheritance is a decision for
                   // whoever introduces the first non-Global scope.
                   && (scopeType == null
                       || (assignment.ScopeType == scopeType
                           && assignment.ScopeId == scopeId))

                   && (permissionCode == null || permission.Code == permissionCode)

               select new
               {
                   Assignment = assignment,
                   Role = role,
                   Permission = permission,
                   Holder = holder,
               };

        // THE ORDERING IS A PARAMETER for exactly the reason the narrowing is:
        // EF cannot translate an operator applied AFTER a projection to a named
        // type, so a caller that ordered afterwards would throw at run time
        // rather than fail to compile. The collation is the case that proves
        // it — Collate() after the projection does not translate at all.
        var ordered = order == CandidateOrder.Holder

            // AUT-Q7 reads people, so it orders by person, then by the roles
            // within them. Ordering here is what lets the grouping be a plain
            // in-memory pass: GroupBy preserves encounter order.
            ? rows
                .OrderBy(x => EF.Functions.Collate(x.Holder.DisplayName, Collation))
                .ThenBy(x => x.Holder.Id)
                .ThenBy(x => EF.Functions.Collate(x.Role.Name, Collation))
                .ThenBy(x => x.Role.Id)

            // The reporting order, and the one that must not change: it decides
            // WHICH assignment IsAllowedAsync names as the authority, and the
            // audit record keeps that answer.
            : rows
                .OrderBy(x => x.Assignment.EffectiveFrom)
                .ThenBy(x => x.Assignment.Id);

        return ordered.Select(x => new Candidate(x.Assignment, x.Role, x.Permission, x.Holder));
    }

    /// <summary>
    /// Which order a view needs. Not a preference: the reporting order decides
    /// which assignment is recorded as having authorised an act.
    /// </summary>
    private enum CandidateOrder
    {
        Reporting,
        Holder,
    }

    private sealed record Candidate(
        UserRole Assignment,
        Role Role,
        Permission Permission,
        User Holder);
}
