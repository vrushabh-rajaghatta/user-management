using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IUserTokenService
{
    /// <summary>
    /// Produces the material for one activation or password-reset token.
    /// </summary>
    /// <param name="tokenId">
    /// The id the UserToken entity will carry. Taken as a parameter because the
    /// delivered token embeds it — CRD-C1 consumes by primary key, so the
    /// recipient has to hand the id back. Ids are generated in memory
    /// (ValueGeneratedNever), so this needs no database round-trip.
    /// </param>
    TokenMaterial Generate(UserTokenId tokenId);

    /// <summary>
    /// The verifier for a secret, in the same form Generate stores (CRD-C1).
    ///
    /// Consumption needs to turn a presented secret back into the value held in
    /// user_token.token_hash. That belongs here rather than in a separate
    /// abstraction because this type already owns the token's hash format —
    /// splitting them would let the two drift and make every outstanding token
    /// unconsumable.
    /// </summary>
    string Hash(string secret);

    /// <summary>
    /// Splits a delivered token back into its id and secret, or null when it is
    /// not in the expected form (CRD-C1).
    ///
    /// Parsing lives beside Generate because Generate composed the string.
    /// Splitting it anywhere else would put the wire format in two places, and
    /// a delimiter that disagrees with itself makes every outstanding token
    /// unconsumable.
    /// </summary>
    PresentedToken? Parse(string plainText);
}

/// <param name="TokenId">Identifies the row; transport, not a secret.</param>
/// <param name="Secret">The credential material, to be hashed and compared.</param>
public sealed record PresentedToken(
    UserTokenId TokenId,
    string Secret);

/// <summary>
/// The two halves of a token, deliberately separated.
/// </summary>
/// <param name="PlainText">
/// "{tokenId}.{secret}" — the complete credential, delivered to the user and
/// NEVER persisted. It exists in the email and nowhere else (UT7).
/// </param>
/// <param name="Hash">
/// The verifier for the secret alone, stored in user_token.token_hash. The id
/// is transport, not credential material, so it is excluded: this keeps
/// TokenHash meaning "verifier for the secret" rather than "verifier for a
/// string that happens to embed a primary key".
/// </param>
public sealed record TokenMaterial(
    string PlainText,
    string Hash);
