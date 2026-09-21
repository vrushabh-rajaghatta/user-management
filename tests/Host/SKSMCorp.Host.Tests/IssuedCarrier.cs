using Microsoft.Net.Http.Headers;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// The one place the Host test suite reads the carrier cookie off a response.
///
/// Sign-in no longer returns the carrier in its body: it arrives only as the
/// __Host-sksmcorp cookie, and a bearer caller takes it from there. Reading
/// Set-Cookie is security-relevant enough that six private copies of the rule
/// would drift, so there is one.
///
/// Deliberately narrow. It finds exactly one cookie with the contract name and
/// hands back what the response said. It does not store a carrier, replay one,
/// or read any other cookie — a test that needs browser semantics uses a real
/// cookie container for that, not this.
/// </summary>
internal static class IssuedCarrier
{
    /// <summary>
    /// A literal rather than CarrierCookie.Name, for CarrierCookieTests' reason:
    /// the name is part of the contract with browsers, so a change to it should
    /// fail a test rather than follow along.
    /// </summary>
    internal const string CookieName = "__Host-sksmcorp";

    /// <summary>
    /// The carrier a successful sign-in issued. Fails the test when the
    /// response does not carry exactly one non-empty carrier cookie — a
    /// deletion is not an issuance.
    /// </summary>
    internal static string From(HttpResponseMessage response)
    {
        var value = Header(response).Value.Value;

        Assert.False(
            string.IsNullOrEmpty(value),
            $"The {CookieName} cookie on this response is empty, which clears a carrier rather than issuing one.");

        return value!;
    }

    /// <summary>
    /// The single carrier cookie on the response, parsed, for the tests that
    /// assert its attributes or that it is a deletion.
    /// </summary>
    internal static SetCookieHeaderValue Header(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        Assert.True(
            response.Headers.TryGetValues(HeaderNames.SetCookie, out var headers),
            $"The response sets no cookie; expected exactly one {CookieName}.");

        var named = SetCookieHeaderValue.ParseStrictList(headers!.ToList())
            .Where(x => x.Name.Equals(CookieName, StringComparison.Ordinal))
            .ToList();

        return Assert.Single(named);
    }

    /// <summary>
    /// The response removes the carrier cookie, with the scope and security
    /// attributes it was written with. A deletion that differed in Path would
    /// set a second cookie rather than remove the first, and one without Secure
    /// and Path=/ is refused by a browser under the __Host- rules — either way
    /// the carrier would survive while the server believed it had cleared it.
    /// </summary>
    internal static void AssertCleared(HttpResponseMessage response)
    {
        var cookie = Header(response);

        Assert.True(
            string.IsNullOrEmpty(cookie.Value.Value),
            $"The {CookieName} cookie on this response carries a value, so it issues a carrier rather than clearing one.");

        Assert.NotNull(cookie.Expires);
        Assert.True(cookie.Expires < DateTimeOffset.UtcNow);

        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.Value);
        Assert.False(cookie.Domain.HasValue);
    }

    /// <summary>
    /// True when the response carries no Set-Cookie header at all — the
    /// assertion for every path that must neither issue nor clear a carrier.
    /// </summary>
    internal static bool IsAbsent(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return !response.Headers.Contains(HeaderNames.SetCookie);
    }
}
