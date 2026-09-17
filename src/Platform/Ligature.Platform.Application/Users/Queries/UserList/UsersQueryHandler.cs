using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Queries.UserList;

/// <summary>
/// USR-Q1 — ListUsers.
///
/// A QUERY HAS NO PIPELINE, so this handler is the whole of the boundary
/// between a caller and the tenant's users, and it runs in a fixed order:
///
///   1. authenticate   no established caller        AuthenticationFailedException (401)
///   2. authorize      user.read, Global scope      BusinessRuleViolationException (400)
///   3. validate       page and pageSize ranges     BusinessRuleViolationException (400)
///   4. read
///
/// Authorization precedes validation so a caller not entitled to the list
/// learns nothing about which parameter values are valid. The exceptions are
/// the ones the command pipeline raises for the same conditions, so the host
/// maps them identically; no refusal mechanism is introduced for queries.
///
/// The permission is read from UsersQuery's own declaration, never restated:
/// the declaration is descriptive, and this is the enforcement it describes
/// (docs/architecture.md section 11). Only IsAllowed is consumed — Authority
/// exists to record the assignment an audited act ran under, and this read is
/// not audited.
///
/// HASMORE WITHOUT A COUNT. The reader is asked for one row more than a page;
/// if it arrives, another page exists, and it is not returned.
/// </summary>
public sealed class UsersQueryHandler : IQueryHandler<UsersQuery, UsersResult>
{
    /// <summary>docs/requirements.md, USR-Q1 "Page-size values".</summary>
    private const int DefaultPageSize = 25;

    /// <summary>docs/requirements.md, USR-Q1 "Page-size values".</summary>
    private const int MaximumPageSize = 100;

    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IUserListReader _reader;

    public UsersQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserListReader reader)
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

    public async Task<UsersResult> Handle(
        UsersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ---- 1. Authenticate. Worded as AuthenticationBehavior's; the host
        // replaces it with its fixed 401 message either way.
        if (!_executionContext.IsAuthenticated)
        {
            throw new AuthenticationFailedException(
                "An authenticated user is required.");
        }

        // ---- 2. Authorize.
        var authorization = await _authorizationService.IsAllowedAsync(
            new AuthorizationRequest(
                _executionContext.UserId,
                UsersQuery.Authorization.PermissionCode,
                _clock.UtcNow,
                "Global",
                null),
            cancellationToken);

        if (!authorization.IsAllowed)
        {
            throw new BusinessRuleViolationException(
                "The current actor does not have permission to list users.");
        }

        // ---- 3. Validate. Refused, never corrected.
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? DefaultPageSize;

        if (page < 1)
        {
            throw new BusinessRuleViolationException(
                "The page number must be 1 or greater.");
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new BusinessRuleViolationException(
                $"The page size must be between 1 and {MaximumPageSize}.");
        }

        // ---- 4. Read. The offset is computed wide: (page − 1) × pageSize can
        // exceed the reader's int, and an overflowed offset would wrap into a
        // wrong or negative window. Past that range there is nothing to read.
        var offset = (long)(page - 1) * pageSize;

        if (offset > int.MaxValue)
            return new UsersResult([], page, pageSize, HasMore: false);

        var rows = await _reader.ReadAsync(
            (int)offset, pageSize + 1, cancellationToken);

        var hasMore = rows.Count > pageSize;

        return new UsersResult(
            hasMore ? rows.Take(pageSize).ToList() : rows,
            page,
            pageSize,
            hasMore);
    }
}
