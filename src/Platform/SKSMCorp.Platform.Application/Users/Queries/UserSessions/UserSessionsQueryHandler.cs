using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Users.Queries.UserSessions;

/// <summary>
/// SES-Q1 GetActiveSessions for one user (docs/requirements.md, "SES-Q1
/// GetActiveSessions and Revoke on the User detail page"). A query has no
/// pipeline, so the handler establishes, in order, as the other reads do:
/// authenticate, authorise session.read, then read. Not audited.
///
/// NO SESSION SEMANTICS OF ITS OWN. What an active-session list means — the
/// canonical test, the order, IdleExpiresAt, Current and the projection — is
/// ActiveSessionListing's, shared with SES-Q2, so the list shows exactly what
/// Revoke can act on. This handler decides only who may read and whose
/// sessions: session.read, and a human target user.
/// </summary>
public sealed class UserSessionsQueryHandler : IQueryHandler<UserSessionsQuery, UserSessionsResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;
    private readonly ActiveSessionListing _listing;

    public UserSessionsQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserRepository userRepository,
        ActiveSessionListing listing)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(listing);

        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _clock = clock;
        _userRepository = userRepository;
        _listing = listing;
    }

    public async Task<UserSessionsResult> Handle(UserSessionsQuery query, CancellationToken cancellationToken)
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
                UserSessionsQuery.Authorization.PermissionCode,
                now,
                "Global",
                null),
            cancellationToken);

        if (!authorization.IsAllowed)
        {
            throw new BusinessRuleViolationException(
                "The current actor does not have permission to view sessions.");
        }

        // ---- 3. A human user, or unknown — the System actor included.
        var user = await _userRepository.FindAsync(query.UserId, cancellationToken);

        if (user is null || user.ActorType != ActorType.Human)
            throw new BusinessRuleViolationException("The user does not exist.");

        // ---- 4. Read, with the one definition of an active-session list.
        return new UserSessionsResult(
            await _listing.ListAsync(query.UserId, query.CallerSessionId, now, cancellationToken));
    }
}
