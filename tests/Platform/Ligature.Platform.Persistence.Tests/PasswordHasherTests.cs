using System.Security.Cryptography;
using System.Text;
using Ligature.Platform.Persistence.Services;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// Pure unit tests — hashing touches no database.
///
/// The property under test is that the stored value is SELF-DESCRIBING: every
/// parameter verification needs is recoverable from it. If any of them lived
/// only in this class, raising the iteration count in a future release would
/// make existing credentials unverifiable (CRD-C1).
/// </summary>
public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void The_algorithm_marker_names_the_scheme_and_a_version()
    {
        var material = _hasher.Hash("correct horse battery staple");

        Assert.Equal(PasswordHasher.AlgorithmMarker, material.Algorithm);

        // SES-C1 compares this against the current marker to decide whether to
        // re-hash on sign-in, so it has to carry a version, not just a scheme.
        Assert.Matches(@"^pbkdf2-sha256-v\d+$", material.Algorithm);
    }

    [Fact]
    public void Every_hash_uses_a_fresh_salt()
    {
        const string password = "the same password twice";

        var first = Parse(_hasher.Hash(password).Hash);
        var second = Parse(_hasher.Hash(password).Hash);

        Assert.NotEqual(first.Salt, second.Salt);

        // Which is the point: identical passwords must not produce identical
        // stored values, or the database reveals who shares a password.
        Assert.NotEqual(first.DerivedKey, second.DerivedKey);
    }

    /// <summary>
    /// The heart of it: re-deriving with only what the stored string carries
    /// must reproduce the stored key. Nothing may be hidden in code.
    /// </summary>
    [Fact]
    public void The_stored_value_carries_everything_verification_needs()
    {
        const string password = "a password worth checking";

        var parsed = Parse(_hasher.Hash(password).Hash);

        var rederived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            parsed.Salt,
            parsed.Iterations,
            HashAlgorithmName.SHA256,
            parsed.DerivedKey.Length);

        Assert.Equal(parsed.DerivedKey, rederived);
    }

    [Fact]
    public void A_different_password_does_not_reproduce_the_key()
    {
        var parsed = Parse(_hasher.Hash("the real password").Hash);

        var rederived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes("not the real password"),
            parsed.Salt,
            parsed.Iterations,
            HashAlgorithmName.SHA256,
            parsed.DerivedKey.Length);

        Assert.NotEqual(parsed.DerivedKey, rederived);
    }

    [Fact]
    public void The_work_factor_and_sizes_are_the_configured_ones()
    {
        var parsed = Parse(_hasher.Hash("sized correctly").Hash);

        Assert.Equal(PasswordHasher.Iterations, parsed.Iterations);
        Assert.Equal(PasswordHasher.SaltBytes, parsed.Salt.Length);
        Assert.Equal(PasswordHasher.DerivedKeyBytes, parsed.DerivedKey.Length);
    }

    /// <summary>
    /// The work factor is a security floor, so it is pinned to a literal rather
    /// than to the constant. Asserting against PasswordHasher.Iterations would
    /// move both sides of the comparison together and let a silent weakening
    /// through; a lower bound still permits raising it deliberately.
    /// </summary>
    [Fact]
    public void The_work_factor_never_falls_below_the_security_floor()
    {
        // OWASP's 2023 minimum for PBKDF2-HMAC-SHA256.
        const int Floor = 210_000;

        Assert.True(
            PasswordHasher.Iterations >= Floor,
            $"Iteration count {PasswordHasher.Iterations} is below the {Floor} "
            + "floor. Raising it is fine; lowering it weakens every password "
            + "hashed from now on.");

        Assert.True(PasswordHasher.SaltBytes >= 16);
        Assert.True(PasswordHasher.DerivedKeyBytes >= 32);

        Assert.Equal(
            PasswordHasher.Iterations,
            Parse(_hasher.Hash("floored").Hash).Iterations);
    }

    [Fact]
    public void The_password_itself_never_appears_in_the_stored_value()
    {
        const string password = "SuperSecretPassphrase";

        var material = _hasher.Hash(password);

        Assert.DoesNotContain(
            password, material.Hash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_password_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _hasher.Hash(string.Empty));
        Assert.Throws<ArgumentNullException>(() => _hasher.Hash(null!));
    }

    /// <summary>
    /// Reads the stored string the way a verifier would, using only the string.
    /// </summary>
    private static (int Iterations, byte[] Salt, byte[] DerivedKey) Parse(
        string stored)
    {
        var parts = stored.Split('$');

        Assert.Equal(5, parts.Length);
        Assert.Equal(string.Empty, parts[0]);
        Assert.Equal("pbkdf2-sha256", parts[1]);
        Assert.StartsWith("i=", parts[2]);

        return (
            int.Parse(parts[2]["i=".Length..]),
            Convert.FromBase64String(parts[3]),
            Convert.FromBase64String(parts[4]));
    }
}
