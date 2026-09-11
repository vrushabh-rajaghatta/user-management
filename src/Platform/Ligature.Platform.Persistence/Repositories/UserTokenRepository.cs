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
        TokenType expectedType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenId);
        ArgumentException.ThrowIfNullOrEmpty(tokenHash);

        // TokenType is mapped with HasConversion<string>(), so the enum's name
        // is what the column holds and what this must compare against.
        var expected = expectedType.ToString();

        // UT6 — a SINGLE conditional UPDATE, and deliberately no read of the
        // token before it. "SELECT, decide, UPDATE" would let two simultaneous
        // activation clicks both observe an unused token and both proceed; here
        // they race on one statement, exactly one affects a row, and the loser
        // gets nothing back.
        //
        // RETURNING hands back the identity from the same statement that
        // consumed it, so no second read can observe a different row.
        //
        // UT6's text in the frozen workbook names only the three liveness
        // predicates. The two below are a documented INTERPRETATION of it, not
        // a departure from it — see docs/requirements.md. Both belong in this
        // statement rather than in the caller, because the caller can only run
        // after the token has already been consumed, and refusing then would
        // burn a token for a condition that may be temporary. A subject
        // deactivated in error and reactivated must still be able to activate
        // with the token they were sent.
        //
        //   token_type   the plaintext is an id and a secret and carries no
        //                type, so without this the activation endpoint
        //                consumes a password-reset token that matches on both
        //
        //   EXISTS       the same predicate SignInCommandHandler and
        //                NotificationGate already use for "is this subject
        //                live". A third spelling of one question is a defect
        //                waiting to happen, so this is theirs verbatim.
        //
        // Interpolation here builds an EF parameterised command — the values
        // become bound parameters, not concatenated SQL.
        var identities = await _dbContext.Database
            .SqlQuery<Guid>($"""
                UPDATE user_token
                SET    used_at = {now}
                WHERE  id = {tokenId.Value}
                  AND  token_hash = {tokenHash}
                  AND  token_type = {expected}
                  AND  used_at IS NULL
                  AND  invalidated_at IS NULL
                  AND  expires_at > {now}
                  AND  EXISTS (
                           SELECT 1
                           FROM   user_identity i
                           JOIN   app_user u ON u.id = i.user_id
                           WHERE  i.id = user_token.user_identity_id
                             AND  i.status = 'Active'
                             AND  u.status = 'Active')
                RETURNING user_identity_id AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return identities.Count == 1
            ? new UserIdentityId(identities[0])
            : null;
    }
}
