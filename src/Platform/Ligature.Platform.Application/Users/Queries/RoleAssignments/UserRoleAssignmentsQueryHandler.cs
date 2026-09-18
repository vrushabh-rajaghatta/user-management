using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.RoleAssignments;

/// <summary>AUT-Q2. RED STUB, and not registered.</summary>
public sealed class UserRoleAssignmentsQueryHandler
    : IQueryHandler<UserRoleAssignmentsQuery, UserRoleAssignmentsResult>
{
    public UserRoleAssignmentsQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserRoleAssignmentReader reader)
    {
    }

    public Task<UserRoleAssignmentsResult> Handle(UserRoleAssignmentsQuery query, CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-Q2 is not implemented.");
}
