using Ligature.Host.Authentication;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.SignIn;
using Ligature.Platform.Application.Users.Commands.SignOut;

namespace Ligature.Host.Api;

/// <summary>
/// SES-C1 and SES-C2 over HTTP.
///
/// Command-shaped routes rather than REST resources, because the system is
/// command-shaped. DELETE /sessions/current would describe SES-C2 as a
/// deletion, and it is not one: D6 ends a session by REVOCATION, and the row
/// survives with the actor, reason and instant that ended it.
///
/// These handlers bind, validate, construct a command, dispatch it and map the
/// response. They contain no session validation, no permission check, no
/// business rule, no token cryptography beyond asking AccessCarrier to mint
/// one, and no database access. Everything that decides anything lives behind
/// ICommandDispatcher.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// The same words the pipeline's 401 uses. A failed sign-in must not be
    /// distinguishable from any other authentication failure.
    /// </summary>
    private const string Rejected = "Authentication is required.";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // The descriptions below exist for the OpenAPI document
        // (docs/architecture.md section 18) and are metadata only. They must
        // keep describing the single indistinguishable rejection rather than
        // enumerating its causes: a document that listed "unknown user" and
        // "wrong password" as separate outcomes would hand back precisely what
        // the handlers withhold.
        routes.MapPost("/api/auth/sign-in", SignInAsync)
            .WithTags("Authentication")
            .WithSummary("Sign in and obtain an access carrier.")
            .WithDescription(
                "Anonymous. On success returns a short-lived signed carrier "
                + "for the server-side session. Every failure — unknown user, "
                + "wrong password, locked, inactive — returns the same 401 "
                + "with the same message, and the attempt is recorded either "
                + "way.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        routes.MapPost("/api/auth/sign-out", SignOutAsync)
            .WithTags("Authentication")
            .WithSummary("End the session this request presented.")
            .WithDescription(
                "Requires a carrier. The session is ended by revocation, not "
                + "deletion: the row survives with the actor, reason and "
                + "instant that ended it. The response is empty whether or not "
                + "this call was the one that revoked the session.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Anonymous by definition — sign-in is what produces an authenticated
    /// caller, so it cannot require one.
    /// </summary>
    private static async Task<IResult> SignInAsync(
        SignInRequest? request,
        HttpContext context,
        ICommandDispatcher dispatcher,
        AccessCarrier carrier,
        CancellationToken cancellationToken)
    {
        if (request?.Username is null || request.Password is null)
            return Results.BadRequest(new { Error = "A username and password are required." });

        var result = await dispatcher.SendAsync<SignInCommand, SignInResult>(
            new SignInCommand(
                request.Username,
                request.Password,

                // Security evidence, taken from the connection rather than the
                // body so a caller cannot choose what gets recorded about them.
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString()),
            cancellationToken);

        // SES-C1 returns a result rather than throwing, because both outcomes
        // must COMMIT — the failed-attempt counter is the reason. One status
        // code for every failure: wrong password, unknown user, locked and
        // inactive are already indistinguishable in the result, and mapping
        // them separately here would give back exactly what it withholds.
        if (!result.Succeeded || result.SessionId is null)
            return Results.Json(new { Error = Rejected }, statusCode: 401);

        // Step 8, and the only place it lives. The Host mints the carrier from
        // the SessionId SES-C1 produced; nothing about signing keys reaches the
        // handler.
        return Results.Ok(new { AccessToken = carrier.Issue(result.SessionId) });
    }

    /// <summary>
    /// The SessionId comes from the carrier this request presented, not from a
    /// body: the carrier is opaque to clients, so a caller has no other way to
    /// name their own session. SES-C2 still checks ownership itself — the
    /// command is reachable from any composition edge, and none of them are
    /// trusted to have done it.
    /// </summary>
    private static async Task<IResult> SignOutAsync(
        CurrentCarrier currentCarrier,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        // A binding failure, not an authorisation decision: with no carrier
        // there is no SessionId to construct the command from. The pipeline
        // would refuse this request anyway — SignOutCommand is authenticated —
        // and the message is identical so the two are not tellable apart.
        if (currentCarrier.SessionId is null)
            return Results.Json(new { Error = Rejected }, statusCode: 401);

        await dispatcher.SendAsync<SignOutCommand, SignOutResult>(
            new SignOutCommand(currentCarrier.SessionId),
            cancellationToken);

        // SignOutResult is deliberately empty, and so is this. Reporting
        // whether this call was the one that revoked the session would
        // distinguish "already signed out" from "not yours" from "no such
        // session".
        return Results.NoContent();
    }

    private sealed record SignInRequest(string? Username, string? Password);
}
