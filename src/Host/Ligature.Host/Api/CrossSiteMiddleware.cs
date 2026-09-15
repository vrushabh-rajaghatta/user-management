using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace Ligature.Host.Api;

/// <summary>
/// Refuses state-changing requests that a browser did not send from this
/// application's own origin — the cross-site protection of the cookie
/// transport that amends docs/architecture.md section 17.
///
/// SameSite=Strict on the carrier cookie is not enough on its own, for two
/// reasons. SameSite is scoped to the SITE, so a sibling subdomain's request
/// still carries a Strict cookie; and the anonymous endpoints — sign-in,
/// activation, password reset — carry no cookie for SameSite to withhold, so a
/// forged sign-in would otherwise be possible.
///
/// THE ALGORITHM, and the order is the design:
///
/// <code>
/// 1. GET, HEAD, OPTIONS (exact, case-sensitive)        -> allow
/// 2. Sec-Fetch-Site has a non-blank value              -> it ALONE decides
///        exactly one value, "same-origin"              -> allow
///        anything else                                 -> refuse
/// 3. Otherwise, Origin absent                          -> allow
/// 4. Otherwise, exactly one serialised http(s) origin
///    whose host[:port] equals Request.Host             -> allow
///        anything else                                 -> refuse
/// </code>
///
/// Stronger evidence is never rescued by weaker evidence. Sec-Fetch-Site is
/// computed by the browser against the URL it actually requested and cannot be
/// set by page script, so when it is present Origin is not consulted — a
/// cross-site or unrecognised value is refused even beside an Origin that
/// matches. Both headers are forbidden to scripts, so a victim's browser cannot
/// be made to send them inconsistently; a non-browser client that does is not
/// forging anybody's request.
///
/// "none" is refused deliberately, which is stricter than a general-purpose
/// library needs to be. It means a user-initiated browser action — the address
/// bar, a bookmark — and the only browser client that makes state-changing
/// requests to this API is its own same-origin application, calling fetch.
///
/// The Origin fallback exists for browsers that predate Sec-Fetch-Site. It
/// compares host and port with the Host header and ignores scheme, because
/// behind a TLS-terminating proxy the request scheme is not trustworthy without
/// forwarded-header handling, which the Host does not have. Two consequences
/// are accepted and recorded: such browsers remain exposed to an HTTP-to-HTTPS
/// forgery on the same host, and a reverse proxy that rewrites Host would
/// refuse them. Modern browsers are decided at step 2 and never reach it.
///
/// A request with neither header is allowed: it is not from a browser that can
/// be made to forge it. That is what keeps non-browser and bearer callers
/// working without this middleware inspecting how a caller authenticates.
///
/// It runs BEFORE CallerMiddleware so that a refused request never reaches the
/// session store — otherwise a same-site request carrying the Strict cookie
/// would be looked up and refresh the victim's idle timer before being turned
/// away — and inside ProblemMiddleware so that anything unexpected here still
/// becomes the no-detail 500.
///
/// No configuration, no trusted-origin allowlist, no bypass paths: there is no
/// CORS and no cross-origin client, so nothing needs one, and each would be a
/// way to switch this off.
/// </summary>
public sealed class CrossSiteMiddleware : IMiddleware
{
    private const string Refused = "Cross-site requests are not accepted.";

    private const string SecFetchSite = "Sec-Fetch-Site";

    private const string SameOrigin = "same-origin";

    /// <summary>
    /// Header values are chosen by whoever sent the request. They are logged
    /// because they explain the refusal, and bounded because they are not ours.
    /// </summary>
    private const int LoggedValueLimit = 128;

    /// <summary>
    /// The same web defaults ProblemMiddleware uses, so this refusal has the
    /// same {"error": ...} shape as every other.
    /// </summary>
    private static readonly JsonSerializerOptions Options =
        JsonSerializerOptions.Web;

    private readonly ILogger<CrossSiteMiddleware> _logger;

    public CrossSiteMiddleware(ILogger<CrossSiteMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (IsSafe(context.Request.Method) || IsFromOwnOrigin(context.Request))
        {
            await next(context);

            return;
        }

        LogRefusal(context.Request);

        // Written here rather than thrown. This is not a command outcome, so it
        // has no place in the platform's exception vocabulary, and it never
        // enters the command pipeline — which is also why it is not audited.
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new ErrorBody(Refused), Options));
    }

    /// <summary>
    /// An allowlist of safe methods, not a denylist of unsafe ones: TRACE and
    /// any unknown method are checked. Case-sensitive, as RFC 9110 defines
    /// method names, so "get" is not GET.
    ///
    /// This rests on a rule the Host must keep: a safe method never changes
    /// state.
    /// </summary>
    private static bool IsSafe(string method)
        => string.Equals(method, HttpMethods.Get, StringComparison.Ordinal)
           || string.Equals(method, HttpMethods.Head, StringComparison.Ordinal)
           || string.Equals(method, HttpMethods.Options, StringComparison.Ordinal);

    private static bool IsFromOwnOrigin(HttpRequest request)
    {
        var fetchSite = request.Headers[SecFetchSite];

        // Present means authoritative. An unknown value, several values, or a
        // different case is refused here and never falls through to Origin.
        if (Presents(fetchSite))
        {
            return fetchSite.Count == 1
                   && string.Equals(fetchSite[0], SameOrigin, StringComparison.Ordinal);
        }

        var origin = request.Headers.Origin;

        if (!Presents(origin))
            return true;

        return origin.Count == 1 && OriginMatchesHost(origin[0]!, request.Host);
    }

    /// <summary>
    /// Decided by value, not by whether the key exists: a blank header carries
    /// no evidence, and whether an empty header survives as a key depends on
    /// the HTTP layer that parsed the request.
    /// </summary>
    private static bool Presents(StringValues values)
        => values.Any(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// A serialised origin is scheme://host[:port] and nothing else. "null",
    /// a non-HTTP scheme, user information, a path, a trailing slash or a query
    /// are all refused rather than normalised into something that matches.
    /// </summary>
    private static bool OriginMatchesHost(string origin, HostString host)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (uri.UserInfo.Length != 0)
            return false;

        if (!string.Equals(
                origin,
                uri.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Host names are case-insensitive. Scheme is deliberately not compared.
        return host.HasValue
               && string.Equals(uri.Authority, host.Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The method, the path and the two provenance headers — and nothing else.
    /// Cookie and Authorization are never logged, at any level: a refused
    /// request can still carry a live credential.
    /// </summary>
    private void LogRefusal(HttpRequest request)
        => _logger.LogWarning(
            "Refused a cross-site {Method} request to {Path}. Sec-Fetch-Site: {SecFetchSite}. Origin: {Origin}.",
            Bounded(request.Method),
            Bounded(request.Path.Value),
            Bounded(request.Headers[SecFetchSite].ToString()),
            Bounded(request.Headers.Origin.ToString()));

    private static string Bounded(string? value)
        => value is null ? string.Empty
            : value.Length <= LoggedValueLimit ? value
            : string.Concat(value.AsSpan(0, LoggedValueLimit), "…");

    private sealed record ErrorBody(string Error);
}
