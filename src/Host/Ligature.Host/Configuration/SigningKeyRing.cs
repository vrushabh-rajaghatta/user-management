using System.Text.RegularExpressions;

namespace Ligature.Host.Configuration;

/// <summary>
/// The configured HMAC signing keys (docs/architecture.md section 17).
///
/// One CURRENT key signs; every configured key verifies. That split is what
/// makes rotation cost nothing: a new key is added, becomes current, and
/// outstanding carriers signed by the previous key keep verifying until it is
/// removed. Because sessions are server-side, retiring a key invalidates
/// carriers but destroys no session state.
/// </summary>
public sealed partial class SigningKeyRing
{
    /// <summary>
    /// Names the key that signs. The value is a key IDENTIFIER, not a format
    /// version — keeping those distinct means a future format change and a key
    /// rotation cannot be mistaken for one another.
    /// </summary>
    public const string CurrentKeySetting = "LIGATURE_SIGNING_KEY_CURRENT";

    /// <summary>
    /// Everything after the prefix is the key identifier, so
    /// LIGATURE_SIGNING_KEY_V1 configures the key named "v1". The accepted set
    /// is simply whatever is configured; there is no required second key, and
    /// nothing here presumes a rotation has happened.
    /// </summary>
    public const string KeyPrefix = "LIGATURE_SIGNING_KEY_";

    /// <summary>
    /// Section 17: key material is at least 32 bytes, validated at startup.
    /// </summary>
    public const int MinimumKeyBytes = 32;

    private readonly Dictionary<string, byte[]> _keys;

    private SigningKeyRing(string currentKeyId, Dictionary<string, byte[]> keys)
    {
        CurrentKeyId = currentKeyId;
        _keys = keys;
    }

    public string CurrentKeyId { get; }

    public byte[] CurrentKey => _keys[CurrentKeyId];

    /// <summary>
    /// Returns the verification key for an identifier, or null when nothing is
    /// configured under that name. An unknown identifier is one of the states
    /// section 17 collapses into a single indistinguishable rejection, so the
    /// caller turns this into the same "no caller established" as every other
    /// failure.
    /// </summary>
    public byte[]? Find(string keyId)
        => _keys.TryGetValue(keyId, out var key) ? key : null;

    /// <summary>
    /// Reads and validates the ring, or throws — there is NO default, no
    /// hard-coded development key and no automatically generated key. A
    /// generated-per-restart key would silently sign every user out on deploy;
    /// a default key would be a vulnerability no test would catch. Both fail
    /// silently, which is why the only acceptable behaviour is refusing to
    /// start.
    /// </summary>
    public static SigningKeyRing Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var currentKeyId = configuration[CurrentKeySetting];

        if (string.IsNullOrWhiteSpace(currentKeyId))
        {
            throw new InvalidOperationException(
                $"{CurrentKeySetting} is not configured, so nothing can sign "
                + "access carriers. Set it to the identifier of a configured "
                + $"key — for example 'v1', supplied as {KeyPrefix}V1.");
        }

        currentKeyId = Normalize(currentKeyId);

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var entry in configuration.AsEnumerable())
        {
            if (!entry.Key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.Key.Equals(CurrentKeySetting, StringComparison.OrdinalIgnoreCase))
                continue;

            var keyId = Normalize(entry.Key[KeyPrefix.Length..]);

            if (!KeyIdPattern().IsMatch(keyId))
            {
                throw new InvalidOperationException(
                    $"'{entry.Key}' does not name a usable signing key. A key "
                    + "identifier must be alphanumeric: it is the first "
                    + "component of a dot-separated carrier, so anything else "
                    + "could not be parsed back out of one.");
            }

            keys[keyId] = Decode(entry.Key, entry.Value);
        }

        if (!keys.ContainsKey(currentKeyId))
        {
            throw new InvalidOperationException(
                $"{CurrentKeySetting} names '{currentKeyId}', but no key is "
                + $"configured under {KeyPrefix}{currentKeyId.ToUpperInvariant()}. "
                + "The current key must be one of the configured keys.");
        }

        return new SigningKeyRing(currentKeyId, keys);
    }

    /// <summary>
    /// Messages name the SETTING and the defect, never the value. A startup
    /// error that echoed key material would put it in every log that captured
    /// the failure.
    /// </summary>
    private static byte[] Decode(string settingName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"'{settingName}' is configured but empty. Remove it or supply "
                + "key material.");
        }

        byte[] key;

        try
        {
            key = Convert.FromBase64String(value);
        }
        catch (FormatException failure)
        {
            throw new InvalidOperationException(
                $"'{settingName}' is not valid Base64.", failure);
        }

        if (key.Length < MinimumKeyBytes)
        {
            throw new InvalidOperationException(
                $"'{settingName}' decodes to {key.Length} bytes; section 17 "
                + $"requires at least {MinimumKeyBytes}.");
        }

        return key;
    }

    /// <summary>
    /// Identifiers are lower-cased on the way in and compared ordinally after,
    /// so LIGATURE_SIGNING_KEY_V1 and a carrier reading "v1" agree while the
    /// comparison itself stays exact.
    /// </summary>
    private static string Normalize(string value)
        => value.Trim().ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex KeyIdPattern();
}
