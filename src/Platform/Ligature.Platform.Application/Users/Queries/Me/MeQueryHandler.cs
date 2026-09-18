using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Queries.Me;

/// <summary>
/// Assembles /me from three sources that are deliberately NOT conflated
/// (B6-B, D5):
///
///   the established caller   who is authenticated, and which session
///   a live identity read     the session's identity, username, display name
///   the authorization service the effective permissions, with their scopes
///
/// It is not a second session validator (D4). Caller establishment is the
/// authentication boundary, and this handler READS its decision (was a caller
/// established for this request?) and never makes one of its own (is this
/// session valid?). The session is read for its TIMING, and the timing is
/// reported rather than enforced.
///
/// THE DECISION IS READ FIRST. A carrier whose signature verifies reaches this
/// handler with its SessionId whether or not the boundary accepted its session:
/// CallerMiddleware records the one and asks about the other separately. An
/// ended, revoked, missing or deactivated session therefore arrives with no
/// established caller, and is refused as any unauthenticated request is,
/// before anything about it is read.
/// </summary>
public sealed class MeQueryHandler : IQueryHandler<MeQuery, MeResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthenticatedCallerReader _callerReader;
    private readonly IAuthorizationService _authorizationService;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IClock _clock;

    public MeQueryHandler(
        IExecutionContext executionContext,
        IAuthenticatedCallerReader callerReader,
        IAuthorizationService authorizationService,
        ISecurityPolicyResolver securityPolicyResolver,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(callerReader);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);
        ArgumentNullException.ThrowIfNull(clock);

        _executionContext = executionContext;
        _callerReader = callerReader;
        _authorizationService = authorizationService;
        _securityPolicyResolver = securityPolicyResolver;
        _clock = clock;
    }

    public async Task<MeResult> Handle(
        MeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The boundary's answer, not a second opinion: no session state is
        // consulted here. Refused before any read, so nothing about a session
        // the boundary refused is looked up, let alone described.
        if (!_executionContext.IsAuthenticated)
        {
            throw new AuthenticationFailedException(
                "An authenticated user is required.");
        }

        var now = _clock.UtcNow;

        var caller = await _callerReader.ReadAsync(query.SessionId, cancellationToken);

        // A DEFECT, not an authorisation decision. The caller was established
        // from this very session moments ago (checked above), so its absence
        // means the row went away underneath a request that had already been
        // accepted. Refusing
        // here on session grounds would be re-deciding validity (D4); throwing
        // says the system is inconsistent, which it would be.
        if (caller is null)
        {
            throw new InvalidOperationException(
                "The session that established this caller could not be read. "
                + "Caller establishment accepted it, so this is an "
                + "inconsistency rather than an authentication outcome.");
        }

        var permissions = await _authorizationService.EnumerateAsync(
            new EffectivePermissionsRequest(_executionContext.UserId, now),
            cancellationToken);

        var policy = await _securityPolicyResolver
            .GetEffectiveSettingsAsync(now, cancellationToken);

        return new MeResult(
            new MeIdentity(
                caller.UserIdentityId,
                caller.Username,
                caller.DisplayName),
            permissions,
            new MeSession(
                caller.SessionExpiresAt,
                caller.SessionLastActivityAt + policy.SessionIdleTimeout));
    }
}
