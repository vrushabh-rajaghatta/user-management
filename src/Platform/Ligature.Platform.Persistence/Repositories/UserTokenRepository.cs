using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;

namespace Ligature.Platform.Persistence.Repositories;

/// <summary>
/// Persists activation and password-reset tokens. Deliberately nothing more.
///
/// This type never generates a token and never hashes one: UserTokenService
/// produces the plaintext and its verifier, and only the verifier is handed
/// here. Keeping generation out of the repository is what makes it structurally
/// impossible for the plaintext to reach the database — the repository has no
/// access to it (UT7).
///
/// UT4 — at most one open token per (UserIdentityId, TokenType) — has no
/// partial unique index in the schema yet, so there is no constraint for a
/// pre-check to mirror and no Exists method on the interface. That index
/// belongs with CRD-C2, alongside UT5's invalidate-prior-tokens semantics,
/// so the invariant and the command that depends on it land together.
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
}
