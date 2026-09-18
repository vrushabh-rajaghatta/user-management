using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Queries.UserProfile;

/// <summary>
/// USR-Q1 GetUser, narrow v1. A query has no pipeline, so the handler
/// establishes, in order, as USR-Q2's and AUT-Q2's do: authenticate,
/// authorise user.read, then read. An unknown user — the System actor
/// included — is refused as unknown. Not audited: reads are not events.
/// </summary>
public sealed class UserProfileQueryHandler : IQueryHandler<UserProfileQuery, UserProfileResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IUserProfileReader _reader;

    public UserProfileQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserProfileReader reader)
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

    public async Task<UserProfileResult> Handle(UserProfileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ---- 1. Authenticate.
        if (!_executionContext.IsAuthenticated)
            throw new AuthenticationFailedException("An authenticated user is required.");

        // ---- 2. Authorise.
        var authorization = await _authorizationService.IsAllowedAsync(
            new AuthorizationRequest(
                _executionContext.UserId,
                UserProfileQuery.Authorization.PermissionCode,
                _clock.UtcNow,
                "Global",
                null),
            cancellationToken);

        if (!authorization.IsAllowed)
        {
            throw new BusinessRuleViolationException(
                "The current actor does not have permission to view users.");
        }

        // ---- 3. Read.
        return await _reader.ReadAsync(query.UserId, cancellationToken)
            ?? throw new BusinessRuleViolationException("The user does not exist.");
    }
}
