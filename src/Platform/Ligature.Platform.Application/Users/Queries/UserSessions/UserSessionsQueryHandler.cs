using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Queries.UserSessions;

/// <summary>
/// SES-Q1 GetActiveSessions for one user (docs/requirements.md, "SES-Q1
/// GetActiveSessions and Revoke on the User detail page"). A query has no
/// pipeline, so the handler establishes, in order, as the other reads do:
/// authenticate, authorise session.read, then read. Not audited.
///
/// NO NEW SESSION SEMANTICS. "Active" is the canonical test the per-request
/// check and SES-C3 apply, read through the same repository method SES-C4
/// uses, so the list shows exactly what Revoke can act on. This handler adds
/// only what a read needs: the unknown-user refusal, the order, and two
/// derived fields.
///
/// <list type="bullet">
/// <item><c>IdleExpiresAt</c> is /me's conservative instant — last activity
/// plus the effective idle timeout, without the enforcement tolerance.</item>
/// <item><c>Current</c> compares SESSION ids: the one row, if any, whose id is
/// the session the Host recovered from this request's carrier.</item>
/// </list>
/// </summary>
public sealed class UserSessionsQueryHandler : IQueryHandler<UserSessionsQuery, UserSessionsResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;

    public UserSessionsQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserRepository userRepository,
        IUserSessionRepository userSessionRepository,
        ISecurityPolicyResolver securityPolicyResolver)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(userSessionRepository);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);

        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _clock = clock;
        _userRepository = userRepository;
        _userSessionRepository = userSessionRepository;
        _securityPolicyResolver = securityPolicyResolver;
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

        // ---- 4. Read, with the one definition of an active session.
        var policy = await _securityPolicyResolver.GetEffectiveSettingsAsync(now, cancellationToken);

        var sessions = await _userSessionRepository.FindActiveForUserAsync(
            query.UserId, now, policy.SessionIdleTimeout, cancellationToken);

        var views = sessions
            .OrderByDescending(x => x.LastActivityAt)
            .ThenBy(x => x.Id.Value)
            .Select(x => new UserSessionView(
                x.Id,
                x.CreatedAt,
                x.LastActivityAt,
                x.ExpiresAt,
                x.LastActivityAt + policy.SessionIdleTimeout,
                x.IpAddress?.ToString(),
                x.UserAgent,
                query.CallerSessionId is { } caller && x.Id == caller))
            .ToList();

        return new UserSessionsResult(views);
    }
}
