using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IUserIdentityRepository
{
    Task<bool> ExistsWithUsernameAsync(
        string username,
        CancellationToken cancellationToken);

    Task AddAsync(
        UserIdentity identity,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a LOCAL identity by username for sign-in (SES-C1 step 1).
    ///
    /// Username only — never email. The specification is emphatic that email
    /// must not be an identity lookup key: a departed employee's address may be
    /// reassigned, and the new holder would sign straight into the previous
    /// holder's account and inherit their approval history.
    ///
    /// Status is deliberately NOT filtered. An inactive identity has to reach
    /// the same generic failure as a missing one, and filtering here would make
    /// the two paths differ in cost as well as in outcome.
    /// </summary>
    Task<UserIdentity?> FindLocalByUsernameAsync(
        string username,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads an identity by id, or null (SES-C2).
    /// </summary>
    Task<UserIdentity?> FindAsync(
        UserIdentityId userIdentityId,
        CancellationToken cancellationToken);
}