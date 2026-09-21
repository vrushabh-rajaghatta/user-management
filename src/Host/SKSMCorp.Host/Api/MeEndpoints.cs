using SKSMCorp.Host.Authentication;
using SKSMCorp.Host.Configuration;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Users.Queries.Me;

namespace SKSMCorp.Host.Api;

/// <summary>
/// B6 — the authenticated caller read, and the first QUERY over HTTP.
///
/// It goes through IQueryDispatcher rather than resolving a handler here: the
/// endpoint is an adapter, and the application boundary stays the one commands
/// already established (docs/architecture.md section 11).
///
/// A GET, so the cross-site middleware passes it through as a safe method. It
/// is still an AUTHENTICATED request, and therefore still activity: a client
/// that polled it would keep an idle session alive indefinitely, which is why
/// the frontend contract forbids using it as a heartbeat.
/// </summary>
public static class MeEndpoints
{
    /// <summary>The pipeline's wording, as the other endpoints use it.</summary>
    private const string Rejected = "Authentication is required.";

    public static void MapMeEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/me", MeAsync)
            .WithTags("Authentication")
            .WithSummary("Describe the caller this request is authenticated as.")
            .WithDescription(
                "Requires a carrier. Returns the identity THIS SESSION was "
                + "established with — a user may hold several — the caller's "
                + "effective permissions with their scopes, and the session's "
                + "timing.\n\n"
                + "There is no anonymous form: without an established caller "
                + "this is 401, never 200 describing an unauthenticated "
                + "caller.\n\n"
                + "idleExpiresAt is INFORMATIONAL and conservative. Activity "
                + "writes are throttled and enforcement carries a tolerance, so "
                + "the server may accept a request after the instant it names. "
                + "It is not a deadline to count down to.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)

            // Documentation only. The pipeline refuses an unauthenticated
            // caller; this marker refuses nothing.
            .WithMetadata(new RequiresCarrier());
    }

    private static async Task<IResult> MeAsync(
        CurrentCarrier currentCarrier,
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        // A binding failure, not an authorisation decision, and worded exactly
        // as sign-out's is so the two are not tellable apart.
        if (currentCarrier.SessionId is null)
            return Results.Json(new { Error = Rejected }, statusCode: 401);

        var me = await dispatcher.SendAsync<MeQuery, MeResult>(
            new MeQuery(currentCarrier.SessionId),
            cancellationToken);

        return Results.Ok(
            new
            {
                Identity = new
                {
                    UserIdentityId = me.Identity.UserIdentityId.Value,
                    me.Identity.Username,
                    me.Identity.DisplayName,
                },
                Permissions = me.Permissions
                    .Select(x => new { x.Code, x.ScopeType, x.ScopeId }),
                Session = new
                {
                    me.Session.ExpiresAt,
                    me.Session.IdleExpiresAt,
                },
            });
    }
}
