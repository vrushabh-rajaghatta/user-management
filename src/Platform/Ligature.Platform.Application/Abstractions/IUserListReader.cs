using Ligature.Platform.Application.Users.Queries.UserList;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>USR-Q1. RED STUB.</summary>
public interface IUserListReader
{
    Task<IReadOnlyList<UserListRow>> ReadAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken);
}
