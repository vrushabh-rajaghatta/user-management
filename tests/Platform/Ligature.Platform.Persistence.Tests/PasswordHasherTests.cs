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

    // ------------------------------------------------------------ verifying

    [Fact]
    public void The_correct_password_verifies()
    {
        const string password = "the correct password";
        var material = _hasher.Hash(password);

        var result = _hasher.Verify(password, material.Hash, material.Algorithm);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void A_wrong_password_does_not_verify()
    {
        var material = _hasher.Hash("the correct password");

        var result = _hasher.Verify(
            "the wrong password", material.Hash, material.Algorithm);

        Assert.False(result.IsValid);
        Assert.False(result.NeedsRehash);
    }

    [Fact]
    public void A_current_algorithm_does_not_need_rehashing()
    {
        const string password = "still current";
        var material = _hasher.Hash(password);

        Assert.False(
            _hasher.Verify(password, material.Hash, material.Algorithm)
                .NeedsRehash);
    }

    /// <summary>
    /// Staleness is read from the algorithm marker, which is what a release
    /// bumps when the policy changes — not inferred from the encoded
    /// parameters, which would make an old-but-equivalent hash look current.
    /// </summary>
    [Fact]
    public void A_superseded_algorithm_needs_rehashing()
    {
        const string password = "hashed under an older release";
        var material = _hasher.Hash(password);

        var result = _hasher.Verify(
            password, material.Hash, "pbkdf2-sha256-v0");

        Assert.True(result.IsValid);
        Assert.True(result.NeedsRehash);
    }

    /// <summary>
    /// NeedsRehash is only meaningful on success: SES-C1 re-hashes on a
    /// successful sign-in, the one moment the plaintext is in hand. Reporting
    /// it for a failed attempt would invite a caller to act on it.
    /// </summary>
    [Fact]
    public void A_failed_verification_never_reports_a_rehash()
    {
        var material = _hasher.Hash("the correct password");

        Assert.False(
            _hasher.Verify("wrong", material.Hash, "pbkdf2-sha256-v0")
                .NeedsRehash);
    }

    // ------------------------------------------- structurally invalid stored

    /// <summary>
    /// Corruption in our own data, not a wrong password. Reporting these as a
    /// failed sign-in would lock a user out of an account whose stored
    /// credential we had quietly broken, with nothing to say so.
    /// </summary>
    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("$pbkdf2-sha256$i=210000$onlythreeparts")]
    [InlineData("$argon2id$i=3$c2FsdA==$a2V5")]
    [InlineData("$pbkdf2-sha256$iterations=210000$c2FsdA==$a2V5")]
    [InlineData("$pbkdf2-sha256$i=0$c2FsdA==$a2V5")]
    [InlineData("$pbkdf2-sha256$i=210000$not-base64!$a2V5")]
    [InlineData("$pbkdf2-sha256$i=210000$c2FsdA==$not-base64!")]
    public void A_structurally_invalid_stored_hash_throws(string stored)
    {
        Assert.Throws<InvalidOperationException>(
            () => _hasher.Verify("any password", stored, "pbkdf2-sha256-v1"));
    }

    // ----------------------------------------------------------------- decoy

    /// <summary>
    /// The decoy exists to make a missing credential cost what a real one does.
    /// It must never succeed, and it must not throw — a failure path that threw
    /// would be its own oracle.
    /// </summary>
    /// <summary>
    /// That the decoy costs the same as a real verification is structural — it
    /// runs the identical Pbkdf2 call at the identical work factor — and is
    /// deliberately NOT asserted here. A timing comparison is unreliable under
    /// parallel load and proves little when it passes; SignInDecoyTests
    /// asserts the property that is observable, namely that the handler calls
    /// it on the not-found path.
    /// </summary>
    [Fact]
    public void The_decoy_verification_completes_for_any_input()
    {
        _hasher.VerifyDecoy("anything at all");
        _hasher.VerifyDecoy(string.Empty);
        _hasher.VerifyDecoy(new string('x', 512));
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
