using System.Net.Http.Headers;
using Ligature.Platform.Application.Abstractions;
using Microsoft.Net.Http.Headers;

namespace Ligature.Host.Authentication;

/// <summary>
/// Establishes the caller for requests that arrive with a valid carrier, and
/// does nothing at all for those that do not (docs/architecture.md section 17).
///
/// A MISSING CREDENTIAL IS NOT AN AUTHENTICATION FAILURE. This middleware never
/// rejects a request: it establishes a caller or it does not, and the command
/// pipeline decides what that means.
///
/// <code>
/// anonymous command     + no caller        -> reaches the handler
/// authenticated command + no caller        -> AuthenticationBehavior rejects
/// authenticated command + invalid carrier  -> no caller -> the same rejection
/// </code>
///
/// Deciding here would mean duplicating the pipeline's authentication rule in a
/// second place, where it could disagree — and it would make anonymous
/// endpoints impossible to reach through the same pipe.
///
/// A CARRIER ARRIVES BY ONE OF TWO TRANSPORTS, and the source is chosen BEFORE
/// either is interpreted:
///
/// <code>
/// Authorization has a non-blank value  ->  read the header, and only the header
/// otherwise                            ->  read the carrier cookie, and only it
/// </code>
///
/// There is deliberately no "try the header, and if that fails try the cookie".
/// A header that is present but unusable — forged, malformed, a bare carrier
/// with no scheme, or another scheme entirely — establishes no caller, and the
/// cookie is never consulted. Rescuing an explicit credential with an ambient
/// one would give one request two possible identities, and whichever it got
/// would depend on which one happened to fail. Choosing the source by presence
/// makes that impossible to write: a null from the header is final, because the
/// decision not to read the cookie was taken before the header was parsed.
///
/// The scheme is not used to infer intent. AuthenticationHeaderValue parses
/// almost anything — a carrier sent without "Bearer " parses as a scheme named
/// after the carrier — so exempting "foreign" schemes would quietly let exactly
/// that mistake fall through to the cookie.
///
/// Which transport was used is not recorded. Nothing downstream needs it, and a
/// value nothing reads is a value something eventually branches on.
/// </summary>
public sealed class CallerMiddleware : IMiddleware
{
    private const string BearerScheme = "Bearer";

    private readonly AccessCarrier _carrier;

    public CallerMiddleware(AccessCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);

        _carrier = carrier;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var sessionId = _carrier.Verify(PresentedCarrier(context.Request));

        if (sessionId is not null)
        {
            context.RequestServices
                .GetRequiredService<CurrentCarrier>()
                .Set(sessionId);

            // The result is deliberately discarded. "No caller was
            // established" is not an outcome this middleware acts on — acting
            // on it is what the pipeline is for, and short-circuiting here
            // would answer a revoked session differently from an absent header.
            _ = await context.RequestServices
                .GetRequiredService<ICallerEstablisher>()
                .EstablishAsync(sessionId, context.RequestAborted);
        }

        await next(context);
    }

    /// <summary>
    /// Selects the transport first, then reads only that transport.
    /// </summary>
    private static string? PresentedCarrier(HttpRequest request)
        => PresentsAuthorization(request)
            ? BearerCarrier(request)
            : CookieCarrier(request);

    /// <summary>
    /// A blank Authorization header carries no credential, so it counts as
    /// absent and the cookie may be read. Nothing is rescued by that: there was
    /// no explicit credential to rescue.
    ///
    /// Decided by VALUE, not by whether the key exists. Whether an empty header
    /// survives as a key depends on the HTTP layer that parsed the request, and
    /// the precedence rule must not.
    /// </summary>
    private static bool PresentsAuthorization(HttpRequest request)
        => request.Headers.Authorization.Any(
            value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// The carrier from a Bearer header, or null for anything else — a forged
    /// or malformed value, another scheme, a bare carrier, or several values.
    /// Every one of them means the same thing downstream: no caller.
    /// </summary>
    private static string? BearerCarrier(HttpRequest request)
    {
        // Several values are joined with a comma, which does not parse. Two
        // credentials in one request are not a choice this code makes.
        if (!AuthenticationHeaderValue.TryParse(
                request.Headers.Authorization.ToString(), out var parsed))
        {
            return null;
        }

        // Case-insensitive, as RFC 7235 defines authentication schemes.
        return parsed.Scheme.Equals(
            BearerScheme, StringComparison.OrdinalIgnoreCase)
            ? parsed.Parameter
            : null;
    }

    /// <summary>
    /// The carrier cookie, if it was presented exactly once under exactly its
    /// name; otherwise null.
    ///
    /// Read from the raw Cookie headers, NOT Request.Cookies. That dictionary
    /// matches names case-insensitively and keeps only the last of several
    /// values, so a cookie planted as "__host-ligature" would be returned as
    /// ours, and of two carriers the later would silently win. The server-side
    /// contract does not rest on a browser enforcing the __Host- prefix.
    ///
    /// Exactly once, because two carriers are an ambiguous presentation — the
    /// same reason two Authorization values establish nothing. That holds even
    /// when both carry the same value: the rule is about what was presented,
    /// not about whether the copies happen to agree.
    ///
    /// The lenient parser, deliberately. The strict one rejects the whole
    /// header when any pair is malformed, so an unrelated application's broken
    /// cookie on the same host would sign every user out. The lenient one skips
    /// pairs it cannot read and keeps the rest.
    ///
    /// No unquoting. The Host never writes a quoted carrier, so a quoted value
    /// is presented as-is and fails verification like any other wrong value.
    /// </summary>
    private static string? CookieCarrier(HttpRequest request)
    {
        if (!CookieHeaderValue.TryParseList(request.Headers.Cookie, out var cookies))
            return null;

        string? carrier = null;

        foreach (var cookie in cookies)
        {
            if (!cookie.Name.Equals(CarrierCookie.Name, StringComparison.Ordinal))
                continue;

            if (carrier is not null)
                return null;

            carrier = cookie.Value.Value ?? string.Empty;
        }

        return string.IsNullOrEmpty(carrier) ? null : carrier;
    }
}
