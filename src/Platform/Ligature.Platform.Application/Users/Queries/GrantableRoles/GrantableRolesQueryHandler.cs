using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.GrantableRoles;

/// <summary>RED STUB, and not registered.</summary>
public sealed class GrantableRolesQueryHandler : IQueryHandler<GrantableRolesQuery, GrantableRolesResult>
{
    public GrantableRolesQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IGrantableRoleReader reader)
    {
    }

    public Task<GrantableRolesResult> Handle(GrantableRolesQuery query, CancellationToken cancellationToken)
        => throw new NotImplementedException("The grantable-role list is not implemented.");
}
