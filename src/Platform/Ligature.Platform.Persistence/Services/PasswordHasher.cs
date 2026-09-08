using System.Security.Cryptography;
using System.Text;
using Ligature.Platform.Application.Abstractions;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing (CRD-C1, step 4).
///
/// A password is human-chosen and therefore guessable, so unlike an activation
/// token it needs a deliberately slow, salted derivation. The framework
/// implementation is used rather than Argon2id so this story adds no
/// dependency; moving to Argon2id later is a security-baseline decision, and
/// the stored format below is what makes it possible without a migration.
///
/// The frozen entity model gives credential exactly two columns — PasswordHash
/// and PasswordAlgorithm — so the salt and work factor are encoded INTO the
/// hash string rather than added as columns. The encoding borrows the shape of
/// the PHC string format but is an internal storage format, not an
/// implementation of that specification.
///
///     $pbkdf2-sha256$i={iterations}${base64 salt}${base64 derived key}
///
/// Everything verification needs is therefore recoverable from the stored
/// value: no parameter lives only in this class, so raising the iteration count
/// in a future release leaves existing credentials verifiable.
/// PasswordAlgorithm carries the version marker instead, which is what SES-C1
/// compares to decide whether to re-hash on a successful sign-in.
///
/// No comparison happens here. Verification, fixed-time equality and
/// rehash-on-sign-in belong to SES-C1.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>Version marker stored in credential.PasswordAlgorithm.</summary>
    internal const string AlgorithmMarker = "pbkdf2-sha256-v1";

    /// <summary>Identifies the derivation inside the hash string itself.</summary>
    private const string SchemeId = "pbkdf2-sha256";

    /// <summary>128 bits — the usual floor for a password salt.</summary>
    internal const int SaltBytes = 16;

    /// <summary>256 bits, matching the SHA-256 output it derives from.</summary>
    internal const int DerivedKeyBytes = 32;

    /// <summary>
    /// OWASP's 2023 guidance for PBKDF2-HMAC-SHA256. Stored per hash, so
    /// raising it affects new passwords only.
    /// </summary>
    internal const int Iterations = 210_000;

    public PasswordHashMaterial Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            DerivedKeyBytes);

        return new PasswordHashMaterial(
            $"${SchemeId}$i={Iterations}"
                + $"${Convert.ToBase64String(salt)}"
                + $"${Convert.ToBase64String(derived)}",
            AlgorithmMarker);
    }
}
