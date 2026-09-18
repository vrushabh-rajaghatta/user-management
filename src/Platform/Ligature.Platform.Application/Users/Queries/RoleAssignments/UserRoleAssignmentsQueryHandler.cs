using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Queries.RoleAssignments;

/// <summary>
/// AUT-Q2 (docs/requirements.md, "AUT-Q2"). A query has no pipeline, so the
/// handler establishes, in order: authenticate, authorise role.read, then read
/// the target, as USR-Q1's does.
///
/// THE STATE IS DERIVED HERE, AT THE CLOCK'S NOW, by the domain's one
/// derivation (RoleAssignmentStates, which UserRole.StateAt also uses). The
/// reader returns stored facts only; nothing stores or re-derives a state.
/// </summary>
public sealed class UserRoleAssignmentsQueryHandler
    : IQueryHandler<UserRoleAssignmentsQuery, UserRoleAssignmentsResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IUserRoleAssignmentReader _reader;

    public UserRoleAssignmentsQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserRoleAssignmentReader reader)
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

    public async Task<UserRoleAssignmentsResult> Handle(
        UserRoleAssignmentsQuery query,
        CancellationToken cancellationToken)
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
                UserRoleAssignmentsQuery.Authorization.PermissionCode,
                now,
                "Global",
                null),
            cancellationToken);

        if (!authorization.IsAllowed)
        {
            throw new BusinessRuleViolationException(
                "The current actor does not have permission to view role assignments.");
        }

        // ---- 3. Read the target.
        var records = await _reader.ReadAsync(query.UserId, cancellationToken)
            ?? throw new BusinessRuleViolationException("The user does not exist.");

        var assignments = records
            .Select(x => new UserRoleAssignment(
                x.AssignmentId,
                x.RoleId,
                x.RoleName,
                x.EffectiveFrom,
                x.EffectiveTo,
                RoleAssignmentStates.At(x.EffectiveFrom, x.EffectiveTo, x.RevokedAt, now),
                x.AssignedAt,
                x.AssignedBy,
                x.AssignmentReason,
                x.RevokedAt,
                x.RevokedBy,
                x.RevocationReason))
            .Where(x => query.IncludeInactive
                || x.State is RoleAssignmentState.Active or RoleAssignmentState.Future)
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenBy(x => x.AssignmentId.Value)
            .ToList();

        return new UserRoleAssignmentsResult(assignments);
    }
}
