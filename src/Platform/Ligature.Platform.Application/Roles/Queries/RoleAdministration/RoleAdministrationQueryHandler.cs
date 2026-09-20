using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Roles.Queries.RoleAdministration;

/// <summary>
/// AUT-Q5 ListRoles (docs/requirements.md, "Role administration read",
/// RA1-RA4). Both parameters narrow the result set; neither changes a derived
/// value. permissionCount, activeHolderCount and agentAssignable are each the
/// reader's own rule, and none of them is stored.
///
/// A query has no pipeline, so the handler establishes, in order, as the other
/// reads do: authenticate, authorise role.read, then read. Not audited.
/// </summary>
public sealed class RoleAdministrationQueryHandler : IQueryHandler<RoleAdministrationQuery, RoleAdministrationResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IRoleAdministrationReader _reader;

    public RoleAdministrationQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IRoleAdministrationReader reader)
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

    public async Task<RoleAdministrationResult> Handle(RoleAdministrationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ---- 1. Authenticate.
        if (!_executionContext.IsAuthenticated)
            throw new AuthenticationFailedException("An authenticated user is required.");

        var now = _clock.UtcNow;

        // ---- 2. Authorise.
        var authorization = await _authorizationService.IsAllowedAsync(
            new AuthorizationRequest(
                _executionContext.UserId,
                RoleAdministrationQuery.Authorization.PermissionCode,
                now,
                "Global",
                null),
            cancellationToken);

        if (!authorization.IsAllowed)
        {
            throw new BusinessRuleViolationException(
                "The current actor does not have permission to view roles.");
        }

        // ---- 3. Read.
        // ONE instant for the whole read: every holder's state is judged
        // against the same now, so two roles' counts cannot disagree about
        // when "now" was (RA2).
        return new RoleAdministrationResult(
            await _reader.ReadAsync(query.IncludeInactive, query.AgentAssignableOnly, now, cancellationToken));
    }
}
