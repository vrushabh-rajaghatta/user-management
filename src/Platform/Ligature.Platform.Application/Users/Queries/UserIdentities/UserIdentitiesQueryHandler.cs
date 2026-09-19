using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Queries.UserIdentities;

/// <summary>
/// IDN-Q1 GetUserIdentities, as amended. A query has no pipeline, so the
/// handler establishes, in order, as the other reads do: authenticate,
/// authorise identity.read, then read. The clock is read once and handed to
/// the reader, so every identity's lock state is judged at the same instant.
/// An unknown user — the System actor included — is refused as unknown. Not
/// audited: reads are not events.
/// </summary>
public sealed class UserIdentitiesQueryHandler : IQueryHandler<UserIdentitiesQuery, UserIdentitiesResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IUserIdentitiesReader _reader;

    public UserIdentitiesQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserIdentitiesReader reader)
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

    public async Task<UserIdentitiesResult> Handle(UserIdentitiesQuery query, CancellationToken cancellationToken)
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
                UserIdentitiesQuery.Authorization.PermissionCode,
                now,
                "Global",
                null),
            cancellationToken);

        if (!authorization.IsAllowed)
        {
            throw new BusinessRuleViolationException(
                "The current actor does not have permission to view identities.");
        }

        // ---- 3. Read, with the lock state judged at this instant.
        var identities = await _reader.ReadAsync(query.UserId, now, cancellationToken)
            ?? throw new BusinessRuleViolationException("The user does not exist.");

        return new UserIdentitiesResult(identities);
    }
}
