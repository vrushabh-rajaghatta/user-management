using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Queries.GrantableRoles;

/// <summary>
/// The grantable-role list: authenticate, authorise role.read, then read.
/// Not AUT-Q5 (see <see cref="GrantableRolesQuery"/>).
/// </summary>
public sealed class GrantableRolesQueryHandler : IQueryHandler<GrantableRolesQuery, GrantableRolesResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IGrantableRoleReader _reader;

    public GrantableRolesQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IGrantableRoleReader reader)
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

    public async Task<GrantableRolesResult> Handle(GrantableRolesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_executionContext.IsAuthenticated)
            throw new AuthenticationFailedException("An authenticated user is required.");

        var authorization = await _authorizationService.IsAllowedAsync(
            new AuthorizationRequest(
                _executionContext.UserId,
                GrantableRolesQuery.Authorization.PermissionCode,
                _clock.UtcNow,
                "Global",
                null),
            cancellationToken);

        if (!authorization.IsAllowed)
            throw new BusinessRuleViolationException("The current actor does not have permission to view roles.");

        return new GrantableRolesResult(await _reader.ReadAsync(cancellationToken));
    }
}
