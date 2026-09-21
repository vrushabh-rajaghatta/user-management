using SKSMCorp.Host.Authentication;
using SKSMCorp.Host.Configuration;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Users.Commands.RevokeSession;
using SKSMCorp.Platform.Application.Users.Queries.UserSessions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Host.Api;

/// <summary>
/// SES-C3 and SES-Q1 over HTTP.
///
/// Authorisation is the pipeline's: a caller without session.revoke is refused
/// inside it, with the same 400 a validation failure gets
/// (docs/requirements.md).
/// </summary>
public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/sessions/{sessionId:guid}/revoke", RevokeAsync)
            .WithTags("Sessions")
            .WithSummary("End one session (administrator).")
            .WithDescription(
                "Requires a carrier, the 'session.revoke' permission and a "
                + "reason. Ends the session if it is still active; a session that "
                + "has already ended is left as it is. Success is 204 with no "
                + "body either way. An unknown session, a missing permission or a "
                + "missing reason is 400.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)

            // Documentation only. The pipeline refuses an unauthenticated
            // caller; this marker refuses nothing.
            .WithMetadata(new RequiresCarrier());

        // SES-Q1 GetActiveSessions for one user: exactly the sessions the
        // canonical active-session test accepts, which is what SES-C3 can end.
        routes.MapGet("/api/users/{userId:guid}/sessions", ListAsync)
            .WithTags("Sessions")
            .WithSummary("A user's active sessions.")
            .WithDescription(
                "Requires a carrier and the 'session.read' permission. Returns "
                + "{ sessions }, most recently active first, each with "
                + "sessionId, createdAt, lastActivityAt, expiresAt, "
                + "idleExpiresAt, ipAddress, userAgent and current. "
                + "'idleExpiresAt' is conservative and informational, not a "
                + "deadline. 'current' is true only for the session this "
                + "request presented. Human users only: an unknown user, the "
                + "System actor and a missing permission are 400. Not audited.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());
    }

    /// <summary>
    /// The caller's session id is the one the Host recovered from the carrier
    /// it verified — passed explicitly, as /me passes it. Nothing the client
    /// sends can name it; it decides only which row is current.
    /// </summary>
    private static async Task<IResult> ListAsync(
        Guid userId,
        CurrentCarrier currentCarrier,
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync<UserSessionsQuery, UserSessionsResult>(
            new UserSessionsQuery(new UserId(userId), currentCarrier.SessionId),
            cancellationToken);

        return Results.Ok(new
        {
            Sessions = result.Sessions.Select(x => new
            {
                SessionId = x.SessionId.Value,
                x.CreatedAt,
                x.LastActivityAt,
                x.ExpiresAt,
                x.IdleExpiresAt,
                x.IpAddress,
                x.UserAgent,
                x.Current,
            }),
        });
    }

    /// <summary>
    /// A missing reason is a binding failure refused here; a blank one is
    /// dispatched and refused by the command.
    /// </summary>
    private static async Task<IResult> RevokeAsync(
        Guid sessionId,
        RevokeRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (request?.Reason is null)
        {
            return Results.BadRequest(
                new { Error = "A reason is required." });
        }

        await dispatcher.SendAsync<RevokeSessionCommand, RevokeSessionResult>(
            new RevokeSessionCommand(new UserSessionId(sessionId), request.Reason),
            cancellationToken);

        return Results.NoContent();
    }

    private sealed record RevokeRequest(string? Reason);
}
