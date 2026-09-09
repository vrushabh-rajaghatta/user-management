using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ligature.Platform.Application.Audit;

/// <summary>
/// Behaviour 14's secret-pattern scan over Before, After and Payload.
///
/// The trail is read by inspectors and exported to them; a token, a hash or
/// a password that reached a payload would make the trail the disclosure
/// (spec sections 6.3, 6.5). The pipeline cannot know what a value MEANS,
/// but it can refuse the shapes secrets take and the names they are stored
/// under. The patterns are a release constant (AUD-S09), not configuration.
///
/// Two kinds of match. A property whose NAME is one a secret is stored under
/// — password, token, hash, secret — with any non-empty string value.
/// Exact names, not substrings: TokenIssued legitimately carries a
/// tokenType, and a name filter that matched it would force every payload
/// to be named around the filter. And a string VALUE shaped like a secret:
/// a JWT, a hex digest of 40 characters or more, or a long high-entropy
/// base64-like string that is not a GUID.
///
/// A match is a defect (section 15.1): the command fails, nothing is
/// written, and the message names the path so the payload can be fixed at
/// its source.
/// </summary>
public static partial class SecretScan
{
    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwordhash", "password_hash", "hash", "secret",
        "token", "accesstoken", "access_token", "refreshtoken", "refresh_token",
        "apikey", "api_key", "privatekey", "private_key", "plaintext",
    };

    [GeneratedRegex(@"^eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}$")]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(@"^[0-9a-fA-F]{40,}$")]
    private static partial Regex HexDigest();

    [GeneratedRegex(@"^[A-Za-z0-9+/_=-]{32,}$")]
    private static partial Regex Base64Like();

    /// <summary>Returns the JSON paths that look like secrets; empty when clean.</summary>
    public static IReadOnlyList<string> Scan(JsonElement content, string root)
    {
        var findings = new List<string>();

        Walk(content, root, findings);

        return findings;
    }

    private static void Walk(JsonElement element, string path, List<string> findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = $"{path}.{property.Name}";

                    if (ForbiddenNames.Contains(property.Name)
                        && property.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrEmpty(property.Value.GetString()))
                    {
                        findings.Add($"{childPath} is named like a secret");
                    }

                    Walk(property.Value, childPath, findings);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Walk(item, $"{path}[{index++}]", findings);
                break;

            case JsonValueKind.String:
                if (LooksLikeSecret(element.GetString()!))
                    findings.Add($"{path} is shaped like a secret");
                break;
        }
    }

    private static bool LooksLikeSecret(string value)
    {
        if (value.Length < 32)
            return false;

        if (Guid.TryParse(value, out _))
            return false;

        if (JsonWebToken().IsMatch(value) || HexDigest().IsMatch(value))
            return true;

        // Long, one-token, letters and digits both present: the shape of a
        // key or an encoded secret, and of nothing a person types.
        return Base64Like().IsMatch(value)
            && value.Any(char.IsLetter)
            && value.Any(char.IsDigit);
    }
}
