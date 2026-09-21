namespace SKSMCorp.Host.Authentication;

/// <summary>
/// Writes and clears the access carrier as a browser cookie — the cookie
/// transport that amends docs/architecture.md section 17. The one place that
/// names the cookie and decides its attributes.
///
/// It carries an already-minted carrier and nothing else. It does not mint one
/// (AccessCarrier does), verify one (AccessCarrier does), or read one from a
/// request (CallerMiddleware does). Keeping reading out of here is deliberate:
/// how the Host writes its transport and how a request presents a credential
/// are different questions, and a class that answered both would become a
/// second authentication abstraction.
///
/// Static because it has no dependencies and must hold no state. It has nothing
/// to configure, on purpose: every attribute below is a security property, and
/// a setting for any of them would be a way to switch it off.
/// </summary>
public static class CarrierCookie
{
    /// <summary>
    /// The __Host- prefix makes a browser refuse this cookie unless it is Secure,
    /// has Path=/ and names no Domain, so a sibling subdomain cannot plant or
    /// overwrite it.
    ///
    /// The prefix is a second line of defence, not the contract. The Host sets
    /// Secure and Path=/ and leaves Domain unset explicitly in Options(), and
    /// CarrierCookieTests asserts those attributes on the emitted header. The
    /// server-side contract does not rest on a browser honouring the prefix.
    /// </summary>
    public const string Name = "__Host-sksmcorp";

    /// <summary>
    /// Sets the carrier as a session cookie.
    ///
    /// No Expires and no Max-Age: the server-side session row is the single
    /// source of truth for lifetime (section 17), and a cookie expiry would be a
    /// second copy of that fact that would eventually disagree with it.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The carrier is null or blank. That is a defect in the calling code, not
    /// caller input — the carrier was minted by the Host a moment earlier — so
    /// it throws rather than writing an empty credential.
    /// </exception>
    public static void Write(HttpResponse response, string carrier)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(carrier);

        response.Cookies.Append(Name, carrier, Options());
    }

    /// <summary>
    /// Removes the cookie, whatever carrier it held and whether or not one was
    /// present.
    ///
    /// The deletion is sent with the SAME attributes the cookie was written
    /// with, and that is load-bearing rather than tidy. A browser matches a
    /// deletion to a cookie by name, domain and path, so a different Path would
    /// set a second cookie instead of removing the first; and the __Host- rules
    /// apply to the deletion header too, so one without Secure and Path=/ is
    /// rejected outright and the carrier silently survives sign-out.
    /// </summary>
    public static void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(Name, Options());
    }

    /// <summary>
    /// One definition, so Write and Clear cannot drift apart.
    ///
    /// Domain, Expires and MaxAge are never assigned: absent is the requirement,
    /// not an omission. Path is assigned even though "/" is the default, because
    /// the __Host- prefix requires it and a requirement should not rest on a
    /// framework default.
    ///
    /// IsEssential keeps the carrier from being withheld by any cookie-consent
    /// policy added later; an authentication cookie is not optional tracking.
    /// </summary>
    private static CookieOptions Options() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
    };
}
