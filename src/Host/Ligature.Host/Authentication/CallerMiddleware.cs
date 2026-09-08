using System.Net.Http.Headers;
using Ligature.Platform.Application.Abstractions;

namespace Ligature.Host.Authentication;

/// <summary>
/// Establishes the caller for requests that arrive with a valid carrier, and
/// does nothing at all for those that do not (docs/architecture.md section 17).
///
/// AN ABSENT Authorization HEADER IS NOT AN AUTHENTICATION FAILURE. This
/// middleware never rejects a request: it establishes a caller or it does not,
/// and the command pipeline decides what that means.
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

        var sessionId = _carrier.Verify(ReadBearer(context.Request));

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
    /// Returns null for absent, malformed and non-Bearer values alike. Every
    /// one of them means the same thing downstream: no caller.
    /// </summary>
    private static string? ReadBearer(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header))
            return null;

        if (!AuthenticationHeaderValue.TryParse(header, out var parsed))
            return null;

        return parsed.Scheme.Equals(
            BearerScheme, StringComparison.OrdinalIgnoreCase)
            ? parsed.Parameter
            : null;
    }
}
