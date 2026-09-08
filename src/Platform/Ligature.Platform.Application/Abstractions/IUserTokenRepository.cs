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
    /// </summary>
    /// <returns>
    /// The identity the consumed token belonged to, or null for every
    /// rejection — unknown id, wrong secret, already used, invalidated,
    /// expired. The caller must not distinguish those, or the endpoint becomes
    /// a token-state oracle. The identity is returned by the same statement
    /// that consumes, so no second read can observe a different row.
    /// </returns>
    Task<UserIdentityId?> TryConsumeAsync(
        UserTokenId tokenId,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}