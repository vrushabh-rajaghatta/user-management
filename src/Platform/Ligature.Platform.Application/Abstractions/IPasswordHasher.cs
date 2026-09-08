namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// Hashes and verifies user-chosen passwords (CRD-C1, SES-C1).
///
/// Every detail of the stored representation stays behind this boundary: the
/// scheme, its work factor, how the salt is encoded, and which version counts
/// as current. A handler asks whether a password is right and whether the
/// stored form is stale; it never learns what either answer is made of.
/// </summary>
public interface IPasswordHasher
{
    PasswordHashMaterial Hash(string password);

    /// <summary>
    /// Checks a presented password against a stored credential (SES-C1 step 4).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The stored value is structurally unusable — malformed encoding, unknown
    /// scheme, unparseable parameters. That is corruption in our own data, not
    /// a wrong password, and reporting it as a failed sign-in would hide it.
    /// </exception>
    PasswordVerificationResult Verify(
        string password,
        string storedHash,
        string storedAlgorithm);

    /// <summary>
    /// Performs the same derivation work as <see cref="Verify"/> against a
    /// fixed internal value that no password can match, and discards the
    /// result (SES-C1).
    ///
    /// Without it, a sign-in attempt for an unknown username returns after a
    /// single index lookup while a real one spends the full work factor — a
    /// timing oracle for "does this account exist" that no amount of identical
    /// error messaging conceals. Callers invoke this on every failure path that
    /// never reached a real credential.
    ///
    /// It closes the obvious gap. It does not make the whole operation
    /// timing-uniform, and should not be described as if it did.
    /// </summary>
    void VerifyDecoy(string password);
}

/// <param name="IsValid">Whether the presented password matched.</param>
/// <param name="NeedsRehash">
/// Whether the stored form uses a superseded algorithm or work factor. Only
/// meaningful when <paramref name="IsValid"/> is true: SES-C1 re-hashes on
/// successful sign-in, which is the only moment the plaintext is available to
/// re-derive from.
/// </param>
public sealed record PasswordVerificationResult(
    bool IsValid,
    bool NeedsRehash);

/// <summary>
/// What the credential stores. The encoding of salt and work factor is the
/// hashing adapter's business, so the handler never learns the algorithm's name
/// or its parameters.
/// </summary>
/// <param name="Hash">
/// Goes to credential.PasswordHash. Self-describing: everything verification
/// needs is recoverable from this string.
/// </param>
/// <param name="Algorithm">
/// Goes to credential.PasswordAlgorithm. The staleness marker SES-C1 compares
/// against the current policy to decide whether to re-hash on sign-in.
/// </param>
public sealed record PasswordHashMaterial(
    string Hash,
    string Algorithm);
