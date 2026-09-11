using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IUserTokenRepository
{
    Task AddAsync(
        UserToken token,
        CancellationToken cancellationToken);

    /// <summary>
    /// Consumes a token by marking it used, but ONLY if it is currently
    /// consumable (CRD-C1, step 1 / UT6).
    ///
    /// The database decides. There is deliberately no way to ask whether a
    /// token is valid and then consume it: two simultaneous activation clicks
    /// must race on one conditional update, and exactly one may win.
    ///
    /// CONSUMABLE MEANS THREE THINGS, not one. The token must be the type the
    /// caller expected; it must be live — unused, uninvalidated, unexpired; and
    /// its subject must be active, both the identity and the user behind it.
    /// All three are predicates of the single conditional update, so a token
    /// refused for any of them is NOT consumed and remains usable if the
    /// condition that refused it goes away.
    ///
    /// <paramref name="expectedType"/> is required rather than optional
    /// precisely so a second consumer cannot forget it. The token plaintext is
    /// an id and a secret and carries no type at all, so without this the
    /// activation path would consume a password-reset token that happened to
    /// match on both.
    /// </summary>
    /// <returns>
    /// The identity the consumed token belonged to, or null for every
    /// rejection — unknown id, wrong secret, wrong type, already used,
    /// invalidated, expired, or an inactive subject. The caller must not
    /// distinguish those, or the endpoint becomes a token-state oracle, and
    /// this deliberately cannot: the statement that decides reports only
    /// whether it consumed. The identity is returned by that same statement,
    /// so no second read can observe a different row.
    /// </returns>
    Task<UserIdentityId?> TryConsumeAsync(
        UserTokenId tokenId,
        string tokenHash,
        TokenType expectedType,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}