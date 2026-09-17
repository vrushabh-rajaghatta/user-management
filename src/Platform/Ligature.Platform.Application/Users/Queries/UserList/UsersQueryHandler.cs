using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.UserList;

/// <summary>USR-Q1. RED STUB.</summary>
public sealed class UsersQueryHandler : IQueryHandler<UsersQuery, UsersResult>
{
    public UsersQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IClock clock,
        IUserListReader reader)
    {
    }

    public Task<UsersResult> Handle(
        UsersQuery query,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("USR-Q1 is not implemented.");
}
