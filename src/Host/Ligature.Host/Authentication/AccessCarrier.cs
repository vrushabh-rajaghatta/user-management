using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Ligature.Host.Configuration;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Host.Authentication;

/// <summary>
/// Mints and verifies the access carrier (docs/architecture.md section 17).
///
/// <code>
/// &lt;key-id&gt;.&lt;base64url(SessionId)&gt;.&lt;base64url(HMAC-SHA256)&gt;
/// </code>
///
/// The signature covers the canonical unsigned portion — the key identifier
/// and the payload, joined exactly as they appear — so neither component can be
/// swapped without invalidating it.
///
/// The format is deliberately minimal rather than a standard container. A
/// container designed to carry claims will eventually be given some, and they
/// will appear to work; this has nowhere to put one. It carries the key
/// identifier and the SessionId and nothing else: no UserId, no roles, no
/// issued-at, no expiry. The session row is the single source of truth for
/// lifetime, and a second copy of that fact would eventually disagree with it.
/// </summary>
public sealed class AccessCarrier
{
    /// <summary>
    /// Three components, so a payload can never be mistaken for a signature
    /// however the input is shaped.
    /// </summary>
    private const int ComponentCount = 3;

    private const char Separator = '.';

    private readonly SigningKeyRing _keys;

    public AccessCarrier(SigningKeyRing keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _keys = keys;
    }

    /// <summary>
    /// Issued from a SessionId that SES-C1 has already produced. SignIn knows
    /// nothing of tokens, signing keys or HTTP headers, which is what keeps it
    /// usable outside HTTP.
    /// </summary>
    public string Issue(UserSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var unsigned = string.Concat(
            _keys.CurrentKeyId,
            Separator,
            Base64Url.EncodeToString(sessionId.Value.ToByteArray()));

        return string.Concat(
            unsigned, Separator, Sign(_keys.CurrentKey, unsigned));
    }

    /// <summary>
    /// Returns the carried SessionId, or null for anything that is not an
    /// intact carrier signed by a configured key.
    ///
    /// A null return is not yet an authentication failure and says nothing
    /// about the session: it means only that no SessionId could be recovered.
    /// Whether that session is live is not this class's question — the Host
    /// extracts an identifier and asks the platform.
    /// </summary>
    public UserSessionId? Verify(string? carrier)
    {
        if (string.IsNullOrWhiteSpace(carrier))
            return null;

        var components = carrier.Split(Separator);

        if (components.Length != ComponentCount)
            return null;

        var key = _keys.Find(components[0]);

        // Unknown key identifier. Collapses into the same null as a bad
        // signature: section 17 requires that none of these states be
        // distinguishable from the others.
        if (key is null)
            return null;

        if (!TryDecode(components[2], out var presented))
            return null;

        var unsigned = string.Concat(components[0], Separator, components[1]);

        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(unsigned));

        // Fixed-time, and BEFORE the payload is decoded into anything the rest
        // of the request will act on. Comparing with SequenceEqual would leak
        // how many leading bytes of a forged signature were right, which is
        // enough to construct one byte at a time.
        if (!CryptographicOperations.FixedTimeEquals(expected, presented))
            return null;

        if (!TryDecode(components[1], out var payload))
            return null;

        // Exactly sixteen bytes, or it is not a Guid. Without the length check
        // the Guid constructor would throw on a short payload — an exception on
        // a caller-supplied value, which is precisely what must not happen
        // here.
        if (payload.Length != 16)
            return null;

        try
        {
            return new UserSessionId(new Guid(payload));
        }
        catch (DomainException)
        {
            // An all-zero payload. StronglyTypedId rejects Guid.Empty, and a
            // rejected identifier is one more indistinguishable failure rather
            // than an error anyone hears about.
            return null;
        }
    }

    private static string Sign(byte[] key, string unsigned)
        => Base64Url.EncodeToString(
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(unsigned)));

    private static bool TryDecode(string component, out byte[] value)
    {
        value = [];

        try
        {
            value = Base64Url.DecodeFromChars(component);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
