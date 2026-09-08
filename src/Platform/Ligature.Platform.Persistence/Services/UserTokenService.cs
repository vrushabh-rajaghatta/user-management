using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// Generates activation and password-reset token material (inv. 14).
///
/// The token is itself a credential — whoever holds it can set the user's
/// password — so only its hash is ever persisted. A database disclosure must
/// not hand over every account with an outstanding token.
///
/// SHA-256 is correct HERE and would be wrong for a password. The secret is 256
/// bits of CSPRNG output with no guessable structure, so there is nothing for
/// an adaptive-cost hash to slow down; it would only add latency to every
/// activation click. CRD-C1's password hash is a separate decision with the
/// opposite answer.
///
/// This type deliberately cannot verify, parse, look up, invalidate or expire a
/// token. Those belong to the consumption lifecycle in CRD-C1 and CRD-C2.
/// </summary>
public sealed class UserTokenService : IUserTokenService
{
    /// <summary>
    /// 256 bits. Base64Url-encodes to 43 unpadded characters.
    /// </summary>
    private const int SecretByteCount = 32;

    /// <summary>
    /// Base64Url's alphabet excludes '.', so the delivered token splits
    /// unambiguously on the first one.
    /// </summary>
    private const char Separator = '.';

    public TokenMaterial Generate(UserTokenId tokenId)
    {
        ArgumentNullException.ThrowIfNull(tokenId);

        var secretBytes = RandomNumberGenerator.GetBytes(SecretByteCount);

        // Base64Url because the token travels in an activation link and must
        // survive a URL without escaping.
        var secret = Base64Url.EncodeToString(secretBytes);

        var hash = Hash(secret);

        return new TokenMaterial(
            PlainText: $"{tokenId.Value}{Separator}{secret}",
            Hash: hash);
    }

    /// <inheritdoc />
    public string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    /// <inheritdoc />
    public PresentedToken? Parse(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return null;

        // The secret is Base64Url, whose alphabet excludes '.', so the FIRST
        // separator is unambiguously the boundary.
        var separator = plainText.IndexOf(Separator);

        if (separator <= 0 || separator == plainText.Length - 1)
            return null;

        if (!Guid.TryParse(plainText[..separator], out var id))
            return null;

        return new PresentedToken(
            new UserTokenId(id),
            plainText[(separator + 1)..]);
    }
}
