using Ligature.Platform.Application.Users.Queries.UserIdentities;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// IDN-Q1: one human user's identities with their lock state as of
/// <paramref name="now"/>, oldest first; null when there is no such human
/// user — the System actor included, which this read treats as unknown.
/// </summary>
public interface IUserIdentitiesReader
{
    Task<IReadOnlyList<UserIdentityView>?> ReadAsync(
        UserId userId, DateTimeOffset now, CancellationToken cancellationToken);
}
