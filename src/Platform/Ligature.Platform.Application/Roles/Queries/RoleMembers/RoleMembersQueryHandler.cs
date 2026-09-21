using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Roles.Queries.RoleMembers;

/// <summary>
/// AUT-Q4 (docs/requirements.md, "AUT-Q4 GetRoleMembers"). A query has no
/// pipeline, so the handler establishes, in order: authenticate, authorise
/// EVERY declared permission, then read the target — as its siblings do.
/// </summary>
public sealed class RoleMembersQueryHandler
    : IQueryHandler<RoleMembersQuery, RoleMembersResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IRoleMemberReader _reader;

    public RoleMembersQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IRoleMemberReader reader)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(reader);

        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _clock = clock;
        _reader = reader;
    }

    public async Task<RoleMembersResult> Handle(
        RoleMembersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ---- 1. Authenticate.
        if (!_executionContext.IsAuthenticated)
            throw new AuthenticationFailedException("An authenticated user is required.");

        var now = _clock.UtcNow;

        // ---- 2. Authorise EVERY declared permission (RH9). AND, not OR.
        //
        // Driven by the declaration rather than by two hand-written checks, so
        // the query type stays the single source of truth: adding a code to
        // the declaration enforces it, and a code enforced here that the
        // declaration does not name is impossible by construction.
        //
        // ONE SENTENCE WHATEVER IS MISSING. Which permission a caller lacks is
        // not something a refusal should disclose.
        foreach (var permissionCode in RoleMembersQuery.Authorization.PermissionCodes)
        {
            var authorization = await _authorizationService.IsAllowedAsync(
                new AuthorizationRequest(
                    _executionContext.UserId,
                    permissionCode,
                    now,
                    "Global",
                    null),
                cancellationToken);

            if (!authorization.IsAllowed)
            {
                throw new BusinessRuleViolationException(
                    "The current actor does not have permission to view role members.");
            }
        }

        // ---- 3. Refuse a scope that cannot exist (RH3).
        //
        // The parameter is kept because the frozen catalogue names it, and V1
        // has one scope. Accepting a value and ignoring it would be a claim to
        // have filtered.
        if (query.ScopeId is not null)
            throw new BusinessRuleViolationException("Only the global scope exists.");

        // ---- 4. Read the target.
        var asOf = query.AsOf ?? now;

        var records = await _reader.ReadAsync(query.RoleId, cancellationToken);

        // RH8: null is "no such role", which the route answers as 404. It is
        // not the same answer as a role nobody holds.
        if (records is null)
            return new RoleMembersResult(asOf, 0, null);

        // THE STATE IS DERIVED HERE, by the domain's one derivation, the same
        // one AUT-Q2 and UserRole.StateAt use (RH2). The order is the reader's
        // and is preserved: Where does not reorder.
        var members = records
            .Where(x => RoleAssignmentStates.At(x.EffectiveFrom, x.EffectiveTo, x.RevokedAt, asOf)
                == RoleAssignmentState.Active)
            .Select(x => new RoleMember(
                x.AssignmentId,
                x.UserId,
                x.DisplayName,
                x.Email,
                x.Status,
                x.EffectiveFrom,
                x.EffectiveTo,
                x.AssignedAt,
                x.AssignedBy,
                x.AssignmentReason))
            .ToList();

        // DISTINCT HOLDERS, over the rows being returned (RH5, RH14). Derived
        // from this very list rather than counted separately, so the count and
        // the list cannot disagree — not merely because both read the same
        // instant, but because there is only one set of rows.
        var activeHolderCount = members
            .Select(x => x.UserId)
            .Distinct()
            .Count();

        return new RoleMembersResult(asOf, activeHolderCount, members);
    }
}
