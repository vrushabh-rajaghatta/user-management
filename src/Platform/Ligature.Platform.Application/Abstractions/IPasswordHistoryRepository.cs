using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IPasswordHistoryRepository
{
    Task AddAsync(PasswordHistory history, CancellationToken cancellationToken);

    /// <summary>
    /// The most recent <paramref name="depth"/> history rows for an identity,
    /// newest first — the rows a new password may not match (CRD-C3).
    ///
    /// Includes the CURRENT password: CR5 writes a history row on every
    /// password set or change, activation included, so "not among the last N"
    /// already covers "not the one you have now" and needs no second check.
    ///
    /// Untracked. password_history is insert-only (PH3), so nothing read here
    /// is ever mutated, and a tracked read would only hand a retried unit of
    /// work the previous attempt's instances.
    /// </summary>
    Task<IReadOnlyList<PasswordHistory>> FindRecentAsync(
        UserIdentityId userIdentityId,
        int depth,
        CancellationToken cancellationToken);
}
