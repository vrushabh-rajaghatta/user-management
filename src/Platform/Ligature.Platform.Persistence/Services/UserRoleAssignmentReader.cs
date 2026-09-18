using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.RoleAssignments;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;

namespace Ligature.Platform.Persistence.Services;

/// <summary>AUT-Q2. RED STUB, and not registered.</summary>
public sealed class UserRoleAssignmentReader : IUserRoleAssignmentReader
{
    public UserRoleAssignmentReader(LigatureDbContext dbContext)
    {
    }

    public Task<IReadOnlyList<UserRoleAssignmentRecord>?> ReadAsync(UserId userId, CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-Q2 is not implemented.");
}
