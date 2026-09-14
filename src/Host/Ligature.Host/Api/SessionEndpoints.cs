using Ligature.Host.Configuration;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.RevokeSession;
using Ligature.Platform.Domain.Users;

namespace Ligature.Host.Api;

/// <summary>
/// SES-C3 over HTTP.
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
