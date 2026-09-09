using Ligature.Platform.Application.Audit;

namespace Ligature.Platform.Application.Tests.Audit;

/// <summary>
/// Behaviour 14's secret scan. A token, a hash or a password in a payload
/// would make the trail the disclosure, so the shapes secrets take and the
/// names they are stored under are refused — and the ordinary values a
/// payload legitimately carries are not, because a scan that flags a
/// display name is a scan somebody switches off.
/// </summary>
public sealed class SecretScanTests
{
    [Theory]
    [InlineData("password", "hunter2")]
    [InlineData("Password", "hunter2")]
    [InlineData("passwordHash", "$2a$10$abc")]
    [InlineData("token", "x")]
    [InlineData("secret", "x")]
    [InlineData("apiKey", "x")]
    [InlineData("hash", "x")]
    public void A_property_named_like_a_secret_is_flagged(string name, string value)
    {
        var element = CanonicalJson.ToElement(new Dictionary<string, object> { [name] = value });

        var findings = SecretScan.Scan(element, "Payload");

        Assert.Contains(findings, x => x.Contains($"Payload.{name}") && x.Contains("named"));
    }

    /// <summary>
    /// Exact names, not substrings. TokenIssued's payload legitimately carries
    /// tokenType and expiresAt; a substring filter would force every payload
    /// to be named around the scan.
    /// </summary>
    [Fact]
    public void tokenType_and_expiresAt_are_not_secrets()
    {
        var element = CanonicalJson.ToElement(new { tokenType = "Activation", expiresAt = "2026-09-12T00:00:00Z" });

        Assert.Empty(SecretScan.Scan(element, "Payload"));
    }

    [Theory]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c")]
    [InlineData("a94a8fe5ccb19ba61c4c0873d391e987982fbbd3a94a8fe5ccb19ba61c4c0873")]
    [InlineData("QmFzZTY0LWxvb2tpbmcgc2VjcmV0IHZhbHVlIDEyMzQ1Njc4OTA=")]
    public void A_value_shaped_like_a_secret_is_flagged(string value)
    {
        var element = CanonicalJson.ToElement(new { note = value });

        var findings = SecretScan.Scan(element, "After");

        Assert.Contains(findings, x => x.StartsWith("After.note") && x.Contains("shaped"));
    }

    [Theory]
    [InlineData("Ada Lovelace")]
    [InlineData("ada.lovelace@example.test")]
    [InlineData("ada.lovelace")]
    [InlineData("SUP-4471: lockout after mistyped password, user confirmed by phone")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("Activation")]
    public void Ordinary_values_are_not_flagged(string value)
    {
        var element = CanonicalJson.ToElement(new { value });

        Assert.Empty(SecretScan.Scan(element, "After"));
    }

    [Fact]
    public void Nested_content_is_scanned_and_the_path_is_reported()
    {
        var element = CanonicalJson.ToElement(new
        {
            outer = new { list = new object[] { new { password = "x" } } },
        });

        var findings = SecretScan.Scan(element, "Payload");

        Assert.Single(findings);
        Assert.StartsWith("Payload.outer.list[0].password", findings[0]);
    }
}
