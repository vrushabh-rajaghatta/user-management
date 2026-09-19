using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Queries.UsernameAvailability;

/// <summary>
/// IDN-Q3 CheckUsernameAvailable (docs/requirements.md, "IDN-Q3
/// CheckUsernameAvailable on Create user"). A query has no pipeline, so the
/// handler establishes, in order, as the other reads do: authenticate,
/// authorise identity.read, then read. Not audited.
///
/// THE COMPATIBILITY SEAM WITH USR-C1. Availability is exactly
/// <see cref="IUserIdentityRepository.ExistsWithUsernameAsync"/> — the check
/// USR-C1 makes before it creates, in the UI7 index's own terms: PostgreSQL's
/// lower(), local identities, every status (D2: absolute). The username is
/// checked as given, untransformed, because that is what USR-C1 receives, and
/// only after UserIdentity.ValidateUsernameBoundary — USR-C1's own rule — has
/// accepted it. Nothing about the holder is returned.
/// </summary>
public sealed class UsernameAvailabilityQueryHandler
    : IQueryHandler<UsernameAvailabilityQuery, UsernameAvailabilityResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IUserIdentityRepository _userIdentityRepository;

    public UsernameAvailabilityQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserIdentityRepository userIdentityRepository)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(userIdentityRepository);

        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _clock = clock;
        _userIdentityRepository = userIdentityRepository;
    }

    public async Task<UsernameAvailabilityResult> Handle(
        UsernameAvailabilityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ---- 1. Authenticate.
        if (!_executionContext.IsAuthenticated)
            throw new AuthenticationFailedException("An authenticated user is required.");

        // ---- 2. Authorise.
        var authorization = await _authorizationService.IsAllowedAsync(
            new AuthorizationRequest(
                _executionContext.UserId,
                UsernameAvailabilityQuery.Authorization.PermissionCode,
                _clock.UtcNow,
                "Global",
                null),
            cancellationToken);

        if (!authorization.IsAllowed)
        {
            throw new BusinessRuleViolationException(
                "The current actor does not have permission to check usernames.");
        }

        // ---- 3. Read, exactly as USR-C1 checks: its own rule first, so a
        // value USR-C1 would refuse is refused here with the same sentence and
        // never looked up ("Local usernames refuse surrounding whitespace", WS6).
        if (string.IsNullOrWhiteSpace(query.Username))
            throw new BusinessRuleViolationException("A username is required.");

        UserIdentity.ValidateUsernameBoundary(query.Username);

        var taken = await _userIdentityRepository.ExistsWithUsernameAsync(query.Username, cancellationToken);

        return new UsernameAvailabilityResult(!taken);
    }
}
