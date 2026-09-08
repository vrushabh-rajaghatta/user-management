namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// Hashes a user-chosen password for storage (CRD-C1, step 4).
///
/// Hash only. Verification is SES-C1's requirement and will bring with it
/// fixed-time comparison, stale-algorithm detection and rehash-on-sign-in — a
/// Verify written now would have no consumer to state those requirements.
/// </summary>
public interface IPasswordHasher
{
    PasswordHashMaterial Hash(string password);
}

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
