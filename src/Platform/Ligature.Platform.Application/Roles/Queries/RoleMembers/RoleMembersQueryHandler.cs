using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Queries.RoleMembers;

/// <summary>
/// AUT-Q4 (docs/requirements.md, "AUT-Q4 GetRoleMembers"). A query has no
/// pipeline, so the handler establishes, in order: authenticate, authorise
/// EVERY declared permission, then read the target — as its siblings do.
/// </summary>
public sealed class RoleMembersQueryHandler
    : IQueryHandler<RoleMembersQuery, RoleMembersResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IRoleMemberReader _reader;

    public RoleMembersQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IRoleMemberReader reader)
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

    public Task<RoleMembersResult> Handle(
        RoleMembersQuery query,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-Q4 is not implemented yet.");
}
