using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Roles.Queries.RolePermissions;

/// <summary>
/// AUT-Q3 GetRolePermissions (docs/requirements.md, "Role administration
/// read", RA5 and RA11). AsOf defaults to the instant of the read, and the
/// grants are the ones live at it.
///
/// A query has no pipeline, so the handler establishes, in order, as the other
/// reads do: authenticate, authorise role.read, then read. Not audited.
/// </summary>
public sealed class RolePermissionsQueryHandler : IQueryHandler<RolePermissionsQuery, RolePermissionsResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IRolePermissionReader _reader;

    public RolePermissionsQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IRolePermissionReader reader)
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

    public async Task<RolePermissionsResult> Handle(RolePermissionsQuery query, CancellationToken cancellationToken)
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
                RolePermissionsQuery.Authorization.PermissionCode,
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
        // RA11: null is "no such role", which the route answers as 404; an
        // empty list is "this role has no live grants". The reader keeps them
        // apart, and nothing here collapses them.
        return new RolePermissionsResult(
            await _reader.ReadAsync(query.RoleId, query.AsOf ?? now, cancellationToken));
    }
}
