using System.Security.Cryptography;
using System.Text;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Services;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// Pure unit tests — token generation touches no database.
/// </summary>
public sealed class UserTokenServiceTests
{
    private readonly UserTokenService _service = new();

    /// <summary>
    /// Pins the contract between generation and CRD-C1: the recipient hands the
    /// id back, and consumption updates WHERE Id = ?. If the id portion were
    /// ever reformatted or truncated, every outstanding token would become
    /// unconsumable.
    /// </summary>
    [Fact]
    public void The_id_portion_is_exactly_the_supplied_token_id()
    {
        var tokenId = UserTokenId.New();

        var material = _service.Generate(tokenId);

        var (id, _) = Split(material.PlainText);

        Assert.Equal(tokenId.Value.ToString(), id, StringComparer.Ordinal);
        Assert.Equal(tokenId.Value, Guid.Parse(id));
    }

    [Fact]
    public void The_hash_verifies_the_secret_alone_not_the_delivered_token()
    {
        var material = _service.Generate(UserTokenId.New());

        var (_, secret) = Split(material.PlainText);

        Assert.Equal(
            Sha256Hex(secret),
            material.Hash,
            StringComparer.Ordinal);

        // The id is transport, not credential material. Hashing the composed
        // string would give token_hash a different meaning and break CRD-C1's
        // verification, which will hash the secret it parses out.
        Assert.NotEqual(
            Sha256Hex(material.PlainText),
            material.Hash,
            StringComparer.Ordinal);
    }

    [Fact]
    public void The_hash_is_sixty_four_lowercase_hexadecimal_characters()
    {
        var material = _service.Generate(UserTokenId.New());

        Assert.Equal(64, material.Hash.Length);

        Assert.All(
            material.Hash,
            c => Assert.True(
                char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'),
                $"'{c}' is not a lowercase hexadecimal character."));
    }

    [Fact]
    public void The_secret_is_two_hundred_and_fifty_six_bits_of_base64url()
    {
        var material = _service.Generate(UserTokenId.New());

        var (_, secret) = Split(material.PlainText);

        // 32 bytes, Base64Url, unpadded.
        Assert.Equal(43, secret.Length);
        Assert.DoesNotContain('=', secret);

        Assert.All(
            secret,
            c => Assert.True(
                char.IsAsciiLetterOrDigit(c) || c is '-' or '_',
                $"'{c}' is outside the Base64Url alphabet."));
    }

    [Fact]
    public void Every_generated_secret_is_distinct()
    {
        var tokenId = UserTokenId.New();

        // The same id twice: only the secret should differ, proving the
        // randomness comes from the CSPRNG and not from the id.
        var secrets = Enumerable
            .Range(0, 512)
            .Select(_ => Split(_service.Generate(tokenId).PlainText).Secret)
            .ToList();

        Assert.Equal(secrets.Count, secrets.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_delivered_token_never_contains_its_own_hash()
    {
        var material = _service.Generate(UserTokenId.New());

        Assert.DoesNotContain(
            material.Hash,
            material.PlainText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_null_token_id_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Generate(null!));
    }

    // ------------------------------------------------------------- parsing

    [Fact]
    public void A_generated_token_parses_back_to_its_id_and_secret()
    {
        var tokenId = UserTokenId.New();
        var material = _service.Generate(tokenId);

        var parsed = _service.Parse(material.PlainText);

        Assert.NotNull(parsed);
        Assert.Equal(tokenId.Value, parsed!.TokenId.Value);

        // The secret Parse yields must hash to the value that was stored, or
        // no issued token could ever be consumed.
        Assert.Equal(material.Hash, _service.Hash(parsed.Secret));
    }

    /// <summary>
    /// Parse is the inverse of Generate, so it accepts exactly the one
    /// representation Generate emits. Guid.TryParse would also admit the braced
    /// and dashless forms; neither is ever issued, and accepting them would
    /// make the token grammar accidental rather than deliberate.
    /// </summary>
    [Theory]
    [InlineData("{0}")]      // braced      — "B" format
    [InlineData("{1}")]      // no dashes   — "N" format
    public void A_non_canonical_id_representation_is_rejected(string template)
    {
        var tokenId = UserTokenId.New();

        var id = template
            .Replace("{0}", $"{{{tokenId.Value}}}", StringComparison.Ordinal)
            .Replace("{1}", tokenId.Value.ToString("N"), StringComparison.Ordinal);

        // Sanity: the same secret with the canonical id does parse, so the
        // rejection below is about the representation and nothing else.
        Assert.NotNull(_service.Parse($"{tokenId.Value}.a-secret"));

        Assert.Null(_service.Parse($"{id}.a-secret"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("no-separator")]
    [InlineData(".leading-separator")]
    [InlineData("trailing-separator.")]
    [InlineData("not-a-guid.a-secret")]
    public void A_malformed_token_is_rejected_without_throwing(string? plainText)
    {
        Assert.Null(_service.Parse(plainText!));
    }

    /// <summary>
    /// The secret is hashed exactly as presented. Trimming or case-folding here
    /// would silently change what gets compared against the stored hash.
    /// </summary>
    [Fact]
    public void Parsing_does_not_normalise_the_secret()
    {
        var tokenId = UserTokenId.New();

        foreach (var secret in new[] { "  spaced  ", "MiXeDcAsE", "sec.ret" })
        {
            var parsed = _service.Parse($"{tokenId.Value}.{secret}");

            Assert.NotNull(parsed);
            Assert.Equal(secret, parsed!.Secret, StringComparer.Ordinal);
        }
    }

    private static (string Id, string Secret) Split(string plainText)
    {
        var separator = plainText.IndexOf('.');

        Assert.True(separator > 0, $"'{plainText}' carries no separator.");

        return (plainText[..separator], plainText[(separator + 1)..]);
    }

    private static string Sha256Hex(string value)
        => Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
