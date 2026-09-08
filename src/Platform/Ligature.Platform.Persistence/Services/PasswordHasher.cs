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

    /// <inheritdoc />
    public PasswordVerificationResult Verify(
        string password,
        string storedHash,
        string storedAlgorithm)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(storedHash);
        ArgumentException.ThrowIfNullOrEmpty(storedAlgorithm);

        var stored = Decode(storedHash);

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            stored.Salt,
            stored.Iterations,
            HashAlgorithmName.SHA256,
            stored.DerivedKey.Length);

        // Fixed-time: a length-dependent or early-exit comparison leaks how
        // much of a guess was right, which is enough to reconstruct a hash byte
        // by byte given enough attempts.
        var isValid = CryptographicOperations.FixedTimeEquals(
            candidate, stored.DerivedKey);

        // Staleness is read from the algorithm column rather than inferred from
        // the encoded parameters, because that column is what a future release
        // bumps when the policy changes. Only meaningful on success — SES-C1
        // re-hashes then, the one moment the plaintext is in hand.
        var needsRehash = isValid
            && !string.Equals(storedAlgorithm, AlgorithmMarker, StringComparison.Ordinal);

        return new PasswordVerificationResult(isValid, needsRehash);
    }

    /// <inheritdoc />
    public void VerifyDecoy(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var stored = Decode(DecoyHash.Value);

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            stored.Salt,
            stored.Iterations,
            HashAlgorithmName.SHA256,
            stored.DerivedKey.Length);

        // Compared, not discarded outright, so the work is identical to a real
        // verification and cannot be optimised away.
        _ = CryptographicOperations.FixedTimeEquals(candidate, stored.DerivedKey);
    }

    /// <summary>
    /// A real hash of a value nothing can present, derived once per process.
    /// Lazy so the cost lands on first use rather than at startup, and random
    /// so it is not a constant an attacker could recognise.
    /// </summary>
    private static readonly Lazy<string> DecoyHash =
        new(() => new PasswordHasher()
            .Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .Hash);

    /// <summary>
    /// Reads a stored value back into its parameters.
    ///
    /// Everything here is a structural fault in our own data — not a wrong
    /// password — so it throws. Reporting corruption as a failed sign-in would
    /// leave a user locked out of an account whose stored credential we had
    /// quietly broken, with nothing in the logs to say so.
    /// </summary>
    private static (int Iterations, byte[] Salt, byte[] DerivedKey) Decode(
        string storedHash)
    {
        var parts = storedHash.Split('$');

        if (parts.Length != 5 || parts[0].Length != 0)
        {
            throw new InvalidOperationException(
                "The stored password hash is not in the expected format.");
        }

        if (!string.Equals(parts[1], SchemeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The stored password hash uses scheme '{parts[1]}', which this "
                + $"release cannot verify. Expected '{SchemeId}'.");
        }

        if (!parts[2].StartsWith("i=", StringComparison.Ordinal)
            || !int.TryParse(parts[2]["i=".Length..], out var iterations)
            || iterations <= 0)
        {
            throw new InvalidOperationException(
                "The stored password hash has an unreadable iteration count.");
        }

        try
        {
            return (
                iterations,
                Convert.FromBase64String(parts[3]),
                Convert.FromBase64String(parts[4]));
        }
        catch (FormatException failure)
        {
            throw new InvalidOperationException(
                "The stored password hash has an unreadable salt or key.",
                failure);
        }
    }
}
