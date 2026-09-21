using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Roles.Queries.PermissionCatalogue;

/// <summary>
/// AUT-Q6 ListPermissions (docs/requirements.md, "Role administration read").
/// The catalogue is release-owned (PE2) and this only reads it: there is no
/// CreatePermission command, by design.
///
/// A query has no pipeline, so the handler establishes, in order, as the other
/// reads do: authenticate, authorise role.read, then read. Not audited.
/// </summary>
public sealed class PermissionCatalogueQueryHandler : IQueryHandler<PermissionCatalogueQuery, PermissionCatalogueResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IPermissionCatalogueReader _reader;

    public PermissionCatalogueQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IPermissionCatalogueReader reader)
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

    public async Task<PermissionCatalogueResult> Handle(PermissionCatalogueQuery query, CancellationToken cancellationToken)
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
                PermissionCatalogueQuery.Authorization.PermissionCode,
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
        return new PermissionCatalogueResult(
            await _reader.ReadAsync(query.Resource, query.RequiresHumanActor, cancellationToken));
    }
}
