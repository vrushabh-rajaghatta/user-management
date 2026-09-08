using System.Text.Json;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Host.Api;

/// <summary>
/// Maps the three exceptions the platform throws onto HTTP, and everything
/// else onto a 500 that says nothing.
///
/// <code>
/// AuthenticationFailedException   -> 401, a fixed generic message
/// BusinessRuleViolationException  -> 400, the exception's message
/// DomainException                 -> 400, the exception's message
/// anything else                   -> 500, NO detail whatsoever
/// </code>
///
/// This is an ALLOWLIST, and the distinction is the whole point. The two 400
/// messages are surfaced because they were written as caller-facing business
/// text — "The activation token is not valid." — and reviewed as such. The
/// catch-all reads nothing off the exception: not the message, not the type,
/// not the stack, not the inner exception. An EF or Npgsql failure carries
/// schema, SQL and sometimes parameter values, and a `catch (Exception ex)`
/// returning ex.Message is how all of that reaches a client.
///
/// The 401 message is fixed rather than taken from the exception, so that a
/// missing header, a forged carrier and a revoked session stay
/// indistinguishable (section 17).
/// </summary>
public sealed class ProblemMiddleware : IMiddleware
{
    private const string AuthenticationRequired =
        "Authentication is required.";

    private const string Unexpected =
        "The request could not be completed.";

    /// <summary>
    /// Minimal APIs serialise results with the web defaults, which camel-case
    /// property names. JsonSerializer's own defaults do NOT, so serialising
    /// here without saying so produced {"Error":...} from this middleware and
    /// {"error":...} from an endpoint — two shapes for the same failure,
    /// depending on which layer produced it. A client would have had to read
    /// both.
    /// </summary>
    private static readonly JsonSerializerOptions Options =
        JsonSerializerOptions.Web;

    private readonly ILogger<ProblemMiddleware> _logger;

    public ProblemMiddleware(ILogger<ProblemMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            await next(context);
        }
        catch (AuthenticationFailedException)
        {
            await WriteAsync(
                context, StatusCodes.Status401Unauthorized,
                AuthenticationRequired);
        }
        catch (BusinessRuleViolationException failure)
        {
            await WriteAsync(
                context, StatusCodes.Status400BadRequest, failure.Message);
        }
        catch (DomainException failure)
        {
            await WriteAsync(
                context, StatusCodes.Status400BadRequest, failure.Message);
        }
        catch (Exception failure)
        {
            // Logged in full server-side, where it is needed, and reported to
            // the caller as nothing at all.
            _logger.LogError(
                failure,
                "Unhandled exception serving {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            await WriteAsync(
                context, StatusCodes.Status500InternalServerError, Unexpected);
        }
    }

    /// <summary>
    /// A response already begun cannot be replaced — the status line is gone.
    /// Overwriting it silently would produce a body that contradicts the status
    /// the client already received, so the exception is left to abort the
    /// response instead.
    /// </summary>
    private static async Task WriteAsync(
        HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new ErrorBody(message), Options));
    }

    private sealed record ErrorBody(string Error);
}
