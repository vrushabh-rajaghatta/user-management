using Ligature.Platform.Application.Users.Queries.UserProfile;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// USR-Q1 v1: one human user's profile fields, or null when there is no such
/// human user — the System actor included, which this read treats as unknown.
/// </summary>
public interface IUserProfileReader
{
    Task<UserProfileResult?> ReadAsync(UserId userId, CancellationToken cancellationToken);
}
