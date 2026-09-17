using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.UserList;
using Ligature.Platform.Persistence.Database;

namespace Ligature.Platform.Persistence.Services;

/// <summary>USR-Q1. RED STUB.</summary>
public sealed class UserListReader : IUserListReader
{
    public UserListReader(LigatureDbContext dbContext)
    {
    }

    public Task<IReadOnlyList<UserListRow>> ReadAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("USR-Q1 is not implemented.");
}
