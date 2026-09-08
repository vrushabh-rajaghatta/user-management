using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Repositories;

/// <summary>
/// Persists activation and password-reset tokens, and consumes them.
///
/// This type never generates a token and never hashes one: UserTokenService
/// produces the plaintext and its verifier, and only the verifier is handed
/// here. Keeping generation out of the repository is what makes it structurally
/// impossible for the plaintext to reach the database (UT7) — the repository
/// has no access to it.
///
/// UT4 — at most one open token per (UserIdentityId, TokenType) — has no
/// partial unique index in the schema yet, so there is no constraint for a
/// pre-check to mirror and no Exists method on the interface. That index
/// belongs with CRD-C2, alongside UT5's invalidate-prior-tokens semantics, so
/// the invariant and the command that depends on it land together
/// (docs/requirements.md, Known Gaps).
/// </summary>
public sealed class UserTokenRepository : IUserTokenRepository
{
    private readonly LigatureDbContext _dbContext;

    public UserTokenRepository(LigatureDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    /// <summary>
    /// Adds to the change tracker only. UnitOfWork owns the save, so a token
    /// cannot be committed without the identity it belongs to.
    /// </summary>
    public Task AddAsync(UserToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        _dbContext.Add(token);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<UserIdentityId?> TryConsumeAsync(
        UserTokenId tokenId,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenId);
        ArgumentException.ThrowIfNullOrEmpty(tokenHash);

        // UT6 — a SINGLE conditional UPDATE, and deliberately no read of the
        // token before it. "SELECT, decide, UPDATE" would let two simultaneous
        // activation clicks both observe an unused token and both proceed; here
        // they race on one statement, exactly one affects a row, and the loser
        // gets nothing back.
        //
        // RETURNING hands back the identity from the same statement that
        // consumed it, so no second read can observe a different row.
        //
        // Interpolation here builds an EF parameterised command — the values
        // become bound parameters, not concatenated SQL.
        var identities = await _dbContext.Database
            .SqlQuery<Guid>($"""
                UPDATE user_token
                SET    used_at = {now}
                WHERE  id = {tokenId.Value}
                  AND  token_hash = {tokenHash}
                  AND  used_at IS NULL
                  AND  invalidated_at IS NULL
                  AND  expires_at > {now}
                RETURNING user_identity_id AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return identities.Count == 1
            ? new UserIdentityId(identities[0])
            : null;
    }
}
