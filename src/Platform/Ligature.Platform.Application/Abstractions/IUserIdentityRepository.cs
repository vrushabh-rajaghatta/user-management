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
    /// CRD-C2's subject resolution: every identity eligible to receive a
    /// password-reset token for this input, which may be an email address or a
    /// username.
    ///
    /// Eligible means ALL of: a human actor, an active user, an active
    /// identity, a LOCAL identity, and a non-null email to send to. An
    /// external identity resets at its provider, and an identity with no
    /// address has nowhere to send the link.
    ///
    /// EMAIL AS A LOOKUP KEY HERE, UNLIKE SIGN-IN. FindLocalByUsernameAsync
    /// above refuses email deliberately, because a reassigned address would
    /// let a new joiner authenticate as its previous holder. That reasoning
    /// does not transfer: this lookup decides where to send a message, the
    /// token is what authenticates afterwards, and the active-user filter
    /// means a reassigned address resolves to its CURRENT owner — the person
    /// who controls the mailbox — never to the departed one.
    ///
    /// Returns every match rather than deciding, because "exactly one" is the
    /// caller's rule and belongs where it can be read: a username and a
    /// different user's email can both equal one input string — usernames are
    /// unconstrained labels — and that collision must fail closed rather than
    /// resolve by an arbitrary precedence.
    /// </summary>
    Task<IReadOnlyList<UserIdentityId>> FindPasswordResetCandidatesAsync(
        string emailOrUsername,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads an identity by id, or null (SES-C2).
    /// </summary>
    Task<UserIdentity?> FindAsync(
        UserIdentityId userIdentityId,
        CancellationToken cancellationToken);
}