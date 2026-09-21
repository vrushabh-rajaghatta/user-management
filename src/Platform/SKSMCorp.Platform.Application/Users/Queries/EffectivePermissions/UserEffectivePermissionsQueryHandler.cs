using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Users.Queries.EffectivePermissions;

/// <summary>
/// USR-Q3 (docs/requirements.md, "USR-Q3 GetUserAccessSummary"). A query has
/// no pipeline, so the handler establishes, in order: authenticate, authorise
/// EVERY declared permission, read the user, then ask the shared resolver.
///
/// IT ADDS A CONTRACT AND A BOUNDARY, NOT A QUERY. There is no new reader and
/// no new evaluation: IUserProfileReader already answers existence and status,
/// and EnumerateAsync already answers the set. EnumerateAsync has always taken
/// any user id; until now it was only ever passed the caller's own, and
/// pointing it at someone else is the whole of what this story adds.
/// </summary>
public sealed class UserEffectivePermissionsQueryHandler
    : IQueryHandler<UserEffectivePermissionsQuery, UserEffectivePermissionsResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IUserProfileReader _users;
    private readonly IClock _clock;

    public UserEffectivePermissionsQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IUserProfileReader users,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(clock);

        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _users = users;
        _clock = clock;
    }

    public async Task<UserEffectivePermissionsResult> Handle(
        UserEffectivePermissionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ---- 1. Authenticate.
        if (!_executionContext.IsAuthenticated)
            throw new AuthenticationFailedException("An authenticated user is required.");

        var now = _clock.UtcNow;

        // ---- 2. Authorise EVERY declared permission (UA2). AND, not OR, and
        // driven by the declaration rather than hand-written, so a code
        // enforced here that the query does not declare is impossible.
        //
        // ONE SENTENCE WHATEVER IS MISSING. Which code a caller lacks is not
        // something a refusal should disclose — and here it would disclose
        // precisely the boundary being enforced.
        foreach (var permissionCode in UserEffectivePermissionsQuery.Authorization.PermissionCodes)
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
                    "The current actor does not have permission to view a user's effective permissions.");
            }
        }

        // ---- 3. The user, FIRST, so an unknown subject is refused before any
        // set is computed and a refusal is never confused with an empty set.
        // IUserProfileReader already answers null for an unknown user AND for
        // the System actor, so no special rule is introduced for either (UA7,
        // UA-A11).
        var user = await _users.ReadAsync(query.UserId, cancellationToken)
            ?? throw new BusinessRuleViolationException("The user does not exist.");

        // ---- 4. The set, from the shared evaluation (UA1). Not a new
        // algorithm and not a second role traversal: EnumerateAsync has always
        // taken any user id, and until now was only ever passed the caller's
        // own. Pointing it at someone else is the whole of what this adds.
        var permissions = await _authorizationService.EnumerateAsync(
            new EffectivePermissionsRequest(query.UserId, now),
            cancellationToken);

        // Ordered by code in BYTE ORDER, as AUT-Q3 and AUT-Q6 order permission
        // codes: a code is an identifier, not prose. Scope breaks ties, since
        // one code may be held in more than one scope once scopes exist.
        var ordered = permissions
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.ScopeType, StringComparer.Ordinal)
            .ThenBy(x => x.ScopeId)
            .ToList();

        // The status is the USER's, current and never reconstructed (UA4). It
        // is what tells an inactive user's empty set from an active one's; the
        // resolver's other reasons for emptiness are deliberately not
        // classified.
        return new UserEffectivePermissionsResult(user.UserId, user.Status, ordered);
    }
}
