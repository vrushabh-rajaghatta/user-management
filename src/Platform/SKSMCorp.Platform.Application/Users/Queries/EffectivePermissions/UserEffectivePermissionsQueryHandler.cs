using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Users.Queries.EffectivePermissions;

/// <summary>
/// USR-Q3 (docs/requirements.md, "USR-Q3 GetUserAccessSummary"). A query has
/// no pipeline, so the handler establishes, in order: authenticate, authorise
/// EVERY declared permission, read the user, then ask the shared resolver.
///
/// IT ADDS A CONTRACT AND A BOUNDARY, NOT A QUERY. There is no new reader and
/// no new evaluation: IUserProfileReader already answers existence and status,
/// and EnumerateAsync already answers the set. EnumerateAsync has always taken
/// any user id; until now it was only ever passed the caller's own, and
/// pointing it at someone else is the whole of what this story adds.
/// </summary>
public sealed class UserEffectivePermissionsQueryHandler
    : IQueryHandler<UserEffectivePermissionsQuery, UserEffectivePermissionsResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IUserProfileReader _users;
    private readonly IClock _clock;

    public UserEffectivePermissionsQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IUserProfileReader users,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(clock);

        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _users = users;
        _clock = clock;
    }

    public Task<UserEffectivePermissionsResult> Handle(
        UserEffectivePermissionsQuery query,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("USR-Q3 is not implemented yet.");
}
