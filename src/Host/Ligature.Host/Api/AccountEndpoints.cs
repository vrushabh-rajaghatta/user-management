using Ligature.Host.Authentication;
using Ligature.Host.Configuration;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.ChangePassword;
using Ligature.Platform.Application.Users.Commands.RequestPasswordReset;
using Ligature.Platform.Application.Users.Commands.ResetPassword;
using Ligature.Platform.Application.Users.Commands.SignOutEverywhere;
using Ligature.Platform.Application.Users.Queries.MySessions;

namespace Ligature.Host.Api;

/// <summary>
/// CRD-C1, CRD-C2, CRD-C3, CRD-C4 and SES-C4 (self form) over HTTP.
///
/// The first three are anonymous, and that is the mechanism rather than an
/// oversight: the token IS the authorisation. Whoever holds the emailed secret
/// proves control of the mailbox, which is the whole point — an administrator
/// setting the password instead would know it, and every document that user
/// later approved could be argued to have been signed by someone else.
///
/// CRD-C4 and SES-C4's self form are the exceptions: each acts for the session
/// presenting the request, so each requires a carrier.
/// </summary>
public static class AccountEndpoints
{
    /// <summary>
    /// The pipeline's 401 wording, as SignOut uses it, so a missing carrier is
    /// not distinguishable from any other authentication failure.
    /// </summary>
    private const string Rejected = "Authentication is required.";

    public static void MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Metadata only, for the OpenAPI document (docs/architecture.md
        // section 18). As with sign-in, the description must not enumerate why
        // a token was refused — that is the oracle the single 400 prevents.
        routes.MapPost("/api/account/activate", ActivateAsync)
            .WithTags("Account")
            .WithSummary("Activate an account and set its first password.")
            .WithDescription(
                "Anonymous: the emailed token is the authorisation, because "
                + "holding it proves control of the mailbox. An invalid, "
                + "expired, consumed or unparseable token all return the same "
                + "400. Rate limited per client address: over the limit, 429 "
                + "with Retry-After.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests);

        // CRD-C2. ONE outcome, deliberately: there is no 400 here and no 404,
        // because a status code that varied with what was typed would be the
        // account-enumeration oracle this command's frozen failure mode
        // forbids. A blank body is rejected by the command, not by a distinct
        // response.
        //
        // The description says so plainly rather than being vague about it —
        // the uniformity is a documented control, not an implementation
        // detail a reader should have to infer.
        routes.MapPost("/api/account/password-reset-request", RequestPasswordResetAsync)
            .WithTags("Account")
            .WithSummary("Request a password-reset link.")
            .WithDescription(
                "Anonymous. Always returns 200 with the same body, whether or "
                + "not an account matches, so that the response cannot be used "
                + "to discover which addresses or usernames are registered. "
                + "If a single eligible local account matches, a single-use "
                + "link is emailed to it and any previous link stops working. "
                + "Rate limited per address and per client address: over the "
                + "limit, 429 with Retry-After, whatever was typed.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status429TooManyRequests);

        // CRD-C3. The counterpart of /activate: the emailed reset link lands
        // on the /reset-password page, which reads the token from the URL
        // fragment and posts it here with the new password.
        routes.MapPost("/api/account/reset-password", ResetPasswordAsync)
            .WithTags("Account")
            .WithSummary("Set a new password using an emailed reset token.")
            .WithDescription(
                "Anonymous: the emailed token is the authorisation. An invalid, "
                + "expired, consumed or unparseable token all return the same "
                + "400. A password the policy refuses also returns 400, and "
                + "leaves the token usable. Rate limited per client address: "
                + "over the limit, 429 with Retry-After.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests);

        // CRD-C4. As with sign-in, the description must not enumerate why a
        // change was refused beyond what the caller already knows.
        routes.MapPost("/api/account/change-password", ChangePasswordAsync)
            .WithTags("Account")
            .WithSummary("Change the password of the signed-in account.")
            .WithDescription(
                "Requires a carrier. Changes the password of the account whose "
                + "session presented this request, and ends that account's "
                + "other sessions; this session stays signed in. A wrong current "
                + "password and an account that cannot be changed right now "
                + "return the same 400. A new password the policy refuses also "
                + "returns 400. Success is 204 with no body.\n\n"
                + "Attempts are limited per session: after the policy's "
                + "MaxFailedLoginAttempts consecutive incorrect current "
                + "passwords, THIS session is ended, the response is 401 and "
                + "the carrier cookie is cleared. The account is not locked and "
                + "its other sessions are unaffected. A successful change "
                + "starts the count again.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)

            // Documentation only. The pipeline refuses an unauthenticated
            // caller; this marker refuses nothing.
            .WithMetadata(new RequiresCarrier());

        // SES-C4, self form.
        routes.MapPost("/api/account/sign-out-everywhere", SignOutEverywhereAsync)
            .WithTags("Account")
            .WithSummary("Sign out of every session of the signed-in account.")
            .WithDescription(
                "Requires a carrier. Ends every active session of the account, "
                + "INCLUDING this one, unless keepCurrentSession is true. When "
                + "this session is ended, the carrier cookie is cleared as well; "
                + "when it is kept, so is the cookie. The body is optional; a "
                + "reason may be given but is not required. Success is 204 with "
                + "no body, including when nothing was active.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)

            // Documentation only. The pipeline refuses an unauthenticated
            // caller; this marker refuses nothing.
            .WithMetadata(new RequiresCarrier());

        // SES-Q2: the caller's own active sessions, with SES-Q1's meaning.
        routes.MapGet("/api/account/sessions", MySessionsAsync)
            .WithTags("Account")
            .WithSummary("The signed-in account's active sessions.")
            .WithDescription(
                "Requires a carrier; no permission. Returns { sessions } for the "
                + "caller's own account, most recently active first, each with "
                + "sessionId, createdAt, lastActivityAt, expiresAt, "
                + "idleExpiresAt, ipAddress, userAgent and current — the same "
                + "fields and meaning as GET /api/users/{userId}/sessions. "
                + "'current' marks the session this request presented. Nothing "
                + "in the request selects another account. Not audited.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());
    }

    /// <summary>
    /// The caller's session id is the one the Host recovered from the carrier
    /// it verified, passed explicitly as /me passes it; the account is the
    /// execution context's caller. Neither comes from the request.
    /// </summary>
    private static async Task<IResult> MySessionsAsync(
        CurrentCarrier currentCarrier,
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync<MySessionsQuery, MySessionsResult>(
            new MySessionsQuery(currentCarrier.SessionId),
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
    /// The current session comes from the carrier, never from the body; the body
    /// may say only whether to keep it and why.
    /// </summary>
    private static async Task<IResult> SignOutEverywhereAsync(
        SignOutEverywhereRequest? request,
        CurrentCarrier currentCarrier,
        HttpContext context,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (currentCarrier.SessionId is null)
            return Results.Json(new { Error = Rejected }, statusCode: 401);

        // One value, so the command and the cookie decision cannot disagree
        // about whether this session was included.
        var keepCurrentSession = request?.KeepCurrentSession ?? false;

        await dispatcher.SendAsync<SignOutEverywhereCommand, SignOutEverywhereResult>(
            new SignOutEverywhereCommand(
                currentCarrier.SessionId,
                keepCurrentSession,
                request?.Reason),
            cancellationToken);

        // This session ended with the others, so the cookie naming it goes too —
        // after the revocation, as at sign-out. Kept, it keeps its cookie.
        if (!keepCurrentSession)
            CarrierCookie.Clear(context.Response);

        return Results.NoContent();
    }

    private sealed record SignOutEverywhereRequest(bool? KeepCurrentSession, string? Reason);

    /// <summary>
    /// The session comes from the carrier this request presented, never from a
    /// body: it names whose password changes and which session survives.
    ///
    /// Counted refusals arrive as outcomes, not exceptions (docs/requirements.md,
    /// "CRD-C4 — limiting current-password attempts per session"): Refused is
    /// the SAME 400 a thrown refusal produces, body and all; SessionEnded is the
    /// pipeline's 401 wording, with the cookie naming the ended session cleared
    /// (L4). Every other refusal still throws and leaves through
    /// ProblemMiddleware as before.
    /// </summary>
    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest? request,
        CurrentCarrier currentCarrier,
        HttpContext context,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (currentCarrier.SessionId is null)
            return Results.Json(new { Error = Rejected }, statusCode: 401);

        if (request?.CurrentPassword is null || request.NewPassword is null)
        {
            return Results.BadRequest(
                new { Error = "The current password and a new password are required." });
        }

        var result = await dispatcher.SendAsync<ChangePasswordCommand, ChangePasswordResult>(
            new ChangePasswordCommand(
                currentCarrier.SessionId, request.CurrentPassword, request.NewPassword),
            cancellationToken);

        switch (result.Outcome)
        {
            case ChangePasswordOutcome.Changed:
                return Results.NoContent();

            case ChangePasswordOutcome.Refused:
                return Results.BadRequest(new { Error = ChangePasswordResult.NotChanged });

            case ChangePasswordOutcome.SessionEnded:
                // The session is over, so the cookie naming it goes too — after
                // the revocation committed, as at sign-out.
                CarrierCookie.Clear(context.Response);

                return Results.Json(new { Error = Rejected }, statusCode: 401);

            default:
                throw new InvalidOperationException(
                    $"Unmapped CRD-C4 outcome '{result.Outcome}'.");
        }
    }

    /// <summary>Never persisted, and never echoed back.</summary>
    private sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    /// <summary>
    /// Returns 200 on every path the command runs, including a missing body.
    ///
    /// Rate limited per typed address and per client address (behaviour 11):
    /// the catalogue makes that a precondition of CRD-C2, and D-NOTIF-03 relies
    /// on it to compensate for the timing difference between the matching and
    /// non-matching branches. A refused request is 429 with the same sentence
    /// whether or not the address names an account, and never reaches the
    /// command — see docs/requirements.md, "Behaviour 11".
    /// </summary>
    private static async Task<IResult> RequestPasswordResetAsync(
        PasswordResetRequest? request,
        ICommandDispatcher dispatcher,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync<RequestPasswordResetCommand, RequestPasswordResetResult>(
                new RequestPasswordResetCommand(
                    request?.EmailOrUsername ?? string.Empty,

                    // Taken from the connection, never the body, so a caller
                    // cannot choose what is recorded about them.
                    context.Connection.RemoteIpAddress?.ToString()),
                cancellationToken);

        // A fixed literal, not a value derived from anything the command saw.
        // Nothing here may vary: not the shape, not the wording, not the
        // presence of a field.
        return Results.Ok(
            new { Message = "If the account exists, a reset link has been sent." });
    }

    private static async Task<IResult> ActivateAsync(
        ActivateRequest? request,
        ICommandDispatcher dispatcher,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request?.Token is null || request.NewPassword is null)
        {
            return Results.BadRequest(
                new { Error = "An activation token and a new password are required." });
        }

        var result = await dispatcher
            .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(
                    request.Token,
                    request.NewPassword,

                    // For rate limiting only (behaviour 11); recorded nowhere.
                    context.Connection.RemoteIpAddress?.ToString()),
                cancellationToken);

        // An invalid, expired, consumed or unparseable token all arrive here as
        // the same BusinessRuleViolationException and leave as the same 400,
        // which is what stops the endpoint being an oracle for which tokens
        // exist.
        return Results.Ok(new { UserIdentityId = result.UserIdentityId.Value });
    }

    /// <summary>
    /// The delivered "{tokenId}.{secret}" string. Never persisted, and never
    /// echoed back in the response.
    /// </summary>
    private sealed record ActivateRequest(string? Token, string? NewPassword);

    /// <summary>
    /// One field, because the command takes one. Whether it holds an address
    /// or a username is not the caller's to declare — usernames are
    /// unconstrained labels and may look exactly like an address, so the
    /// resolution tries both.
    /// </summary>
    private sealed record PasswordResetRequest(string? EmailOrUsername);

    /// <summary>
    /// Refusals arrive as BusinessRuleViolationException and leave as 400
    /// through ProblemMiddleware — the surface CRD-C1 already established.
    /// </summary>
    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest? request,
        ICommandDispatcher dispatcher,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request?.Token is null || request.NewPassword is null)
        {
            return Results.BadRequest(
                new { Error = "A reset token and a new password are required." });
        }

        var result = await dispatcher
            .SendAsync<ResetPasswordCommand, ResetPasswordResult>(
                new ResetPasswordCommand(
                    request.Token,
                    request.NewPassword,

                    // For rate limiting only (behaviour 11); recorded nowhere.
                    context.Connection.RemoteIpAddress?.ToString()),
                cancellationToken);

        return Results.Ok(new { UserIdentityId = result.UserIdentityId.Value });
    }

    /// <summary>
    /// The delivered "{tokenId}.{secret}" string. Never persisted, and never
    /// echoed back in the response.
    /// </summary>
    private sealed record ResetPasswordRequest(string? Token, string? NewPassword);
}
