using SKSMCorp.Host.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

// Both namespaces define SameSiteMode. The assertions read the PARSED header,
// so they compare against the header model's enum, not the options enum.
using SameSiteMode = Microsoft.Net.Http.Headers.SameSiteMode;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// The HTTP contract CarrierCookie puts on the wire. Each assertion reads the
/// Set-Cookie header a browser would receive, not the options object that
/// produced it: what matters is what the browser is told, and a test of the
/// options would pass while the header said something else.
///
/// Pure: no database, no host.
/// </summary>
public sealed class CarrierCookieTests
{
    /// <summary>
    /// A realistic carrier using every character the format can contain beyond
    /// letters and digits: base64url's '-' and '_', and the '.' separator. If
    /// any of them were escaped on the way out, the value would not round-trip.
    /// </summary>
    private const string Carrier = "v1.Ab-_9xYz0123456789abcd.Q-w_E.rTy-u_I";

    /// <summary>
    /// Asserted as a literal rather than through CarrierCookie.Name: the name is
    /// part of the contract with browsers and the client, so a change to it
    /// should fail a test rather than follow along silently.
    /// </summary>
    private const string ExpectedName = "__Host-sksmcorp";

    [Fact]
    public void The_written_cookie_is_host_prefixed_http_only_secure_strict_and_scoped_to_the_whole_origin()
    {
        var cookie = Written(Carrier);

        Assert.Equal(ExpectedName, cookie.Name.Value);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.Value);

        // __Host- is refused by the browser when a Domain is present.
        Assert.False(cookie.Domain.HasValue);
    }

    /// <summary>
    /// The session row owns lifetime. A cookie that expired on its own would be
    /// a second, disagreeing copy of that fact.
    /// </summary>
    [Fact]
    public void The_written_cookie_has_no_lifetime_of_its_own()
    {
        var cookie = Written(Carrier);

        Assert.Null(cookie.Expires);
        Assert.Null(cookie.MaxAge);
    }

    [Fact]
    public void The_written_cookie_carries_the_carrier_unchanged()
    {
        var cookie = Written(Carrier);

        Assert.Equal(Carrier, cookie.Value.Value);
    }

    /// <summary>
    /// The deletion must match the original cookie's scope and security
    /// attributes. A mismatched Path would create a second cookie instead of
    /// removing the first, and a deletion without Secure and Path=/ is refused
    /// by the browser under the __Host- rules — either way the carrier would
    /// survive sign-out while the server believed it had cleared it.
    /// </summary>
    [Fact]
    public void Clearing_expires_the_cookie_with_the_same_scope_and_security_attributes()
    {
        var context = new DefaultHttpContext();

        CarrierCookie.Clear(context.Response);

        var cookie = SingleSetCookie(context.Response);

        Assert.Equal(ExpectedName, cookie.Name.Value);
        Assert.True(string.IsNullOrEmpty(cookie.Value.Value));
        Assert.NotNull(cookie.Expires);
        Assert.True(cookie.Expires < DateTimeOffset.UtcNow);

        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.Value);
        Assert.False(cookie.Domain.HasValue);
        Assert.Null(cookie.MaxAge);
    }

    /// <summary>
    /// A blank carrier is a defect in the Host, never caller input, so it
    /// throws — and writes nothing, because an empty credential cookie would
    /// look to the browser like a successful sign-in.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_carrier_is_refused_and_nothing_is_written(string? carrier)
    {
        var context = new DefaultHttpContext();

        Assert.ThrowsAny<ArgumentException>(
            () => CarrierCookie.Write(context.Response, carrier!));

        // Absent, not merely empty. Headers.SetCookie is StringValues, which
        // Assert.Empty would silently compare as a string.
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.SetCookie));
    }

    private static SetCookieHeaderValue Written(string carrier)
    {
        var context = new DefaultHttpContext();

        CarrierCookie.Write(context.Response, carrier);

        return SingleSetCookie(context.Response);
    }

    /// <summary>
    /// Exactly one Set-Cookie header. A second one — a stray duplicate, or a
    /// cookie under another name — would be a transport the contract does not
    /// describe.
    /// </summary>
    private static SetCookieHeaderValue SingleSetCookie(HttpResponse response)
    {
        var header = Assert.Single(response.Headers.SetCookie);

        return SetCookieHeaderValue.Parse(header);
    }
}
