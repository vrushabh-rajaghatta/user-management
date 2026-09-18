using Ligature.Platform.Application.Users.Queries.UserList;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// USR-Q2 — one window of the user list, in the contract order: human users
/// only, DisplayName ascending under ICU "unicode" collation, then UserId.
///
/// It knows nothing about pages, defaults or limits. Those are the handler's
/// rules, and a reader that applied them too would be a second place for them
/// to disagree.
/// </summary>
public interface IUserListReader
{
    Task<IReadOnlyList<UserListRow>> ReadAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken);
}
