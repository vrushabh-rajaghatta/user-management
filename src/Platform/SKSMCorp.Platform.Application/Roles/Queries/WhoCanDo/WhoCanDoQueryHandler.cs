using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Roles.Queries.WhoCanDo;

/// <summary>
/// AUT-Q7 (docs/requirements.md, "AUT-Q7 WhoCanDo"). A query has no pipeline,
/// so the handler establishes, in order: authenticate, authorise EVERY
/// declared permission, refuse a scope that cannot exist, then ask the shared
/// resolver.
///
/// It performs NO authorisation evaluation of its own (RW2): the answer comes
/// from IAuthorizationService, which is the same evaluation the pipeline uses.
/// </summary>
public sealed class WhoCanDoQueryHandler
    : IQueryHandler<WhoCanDoQuery, WhoCanDoResult>
{
    private const string GlobalScope = "Global";

    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IPermissionCatalogueEntryReader _permissions;
    private readonly IClock _clock;

    public WhoCanDoQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IPermissionCatalogueEntryReader permissions,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(clock);

        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _permissions = permissions;
        _clock = clock;
    }

    public async Task<WhoCanDoResult> Handle(
        WhoCanDoQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ---- 1. Authenticate.
        if (!_executionContext.IsAuthenticated)
            throw new AuthenticationFailedException("An authenticated user is required.");

        var now = _clock.UtcNow;

        // ---- 2. Authorise EVERY declared permission (RW6). AND, not OR, and
        // driven by the declaration so that a code enforced here which the
        // query does not declare is impossible by construction.
        foreach (var permissionCode in WhoCanDoQuery.Authorization.PermissionCodes)
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
                    "The current actor does not have permission to view permission holders.");
            }
        }

        // ---- 3. Refuse a scope that cannot exist (RW7). Both parameters stay
        // in the contract because the catalogue names them; neither carries a
        // usable value while V1 is global-only, and accepting one silently
        // would be a claim to have filtered.
        if (query.ScopeType is not null && query.ScopeType != GlobalScope)
            throw new BusinessRuleViolationException("Only the global scope exists.");

        if (query.ScopeId is not null)
            throw new BusinessRuleViolationException("Only the global scope exists.");

        var asOf = query.AsOf ?? now;

        // ---- 4. The catalogue first, so that "no such permission" and
        // "nobody holds it" stay different answers (RW8).
        var permission = await _permissions.FindAsync(query.PermissionCode, cancellationToken);

        if (permission is null)
            return new WhoCanDoResult(asOf, null, null);

        // ---- 5. The evaluation, which is the resolver's and not this
        // handler's (RW2). It answers who could act, never whether the code
        // exists — step 4 is the only place that decides that.
        var holders = await _authorizationService.WhoCanDoAsync(
            new WhoCanDoRequest(query.PermissionCode, asOf, GlobalScope, null),
            cancellationToken);

        return new WhoCanDoResult(asOf, permission, holders);
    }
}
