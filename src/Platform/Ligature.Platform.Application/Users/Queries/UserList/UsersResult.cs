using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Queries.UserList;

/// <summary>USR-Q1. RED STUB.</summary>
public sealed record UsersResult(
    IReadOnlyList<UserListRow> Users,
    int Page,
    int PageSize,
    bool HasMore);

/// <summary>USR-Q1. RED STUB.</summary>
public sealed record UserListRow(
    UserId UserId,
    string DisplayName,
    string? Email);
