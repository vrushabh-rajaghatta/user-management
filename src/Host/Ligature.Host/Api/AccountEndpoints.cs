using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.RequestPasswordReset;

namespace Ligature.Host.Api;

/// <summary>
/// CRD-C1 and CRD-C2 over HTTP.
///
/// Anonymous, and that is the mechanism rather than an oversight: the token IS
/// the authorisation. Whoever holds the emailed secret proves control of the
/// mailbox, which is the whole point — an administrator setting the password
/// instead would know it, and every document that user later approved could be
/// argued to have been signed by someone else.
/// </summary>
public static class AccountEndpoints
{
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
                + "400.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

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
                + "link is emailed to it and any previous link stops working.")
            .Produces(StatusCodes.Status200OK);
    }

    /// <summary>
    /// Returns 200 on every path, including a missing body.
    ///
    /// NOTE FOR WHOEVER ADDS RATE LIMITING (pipeline behaviour 11, not yet
    /// implemented): it belongs in front of this endpoint, per address and per
    /// IP. The catalogue makes it a precondition of CRD-C2, and D-NOTIF-03
    /// relies on it to compensate for the timing difference between the
    /// matching and non-matching branches. Until then this endpoint is
    /// unthrottled — see docs/requirements.md.
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
        CancellationToken cancellationToken)
    {
        if (request?.Token is null || request.NewPassword is null)
        {
            return Results.BadRequest(
                new { Error = "An activation token and a new password are required." });
        }

        var result = await dispatcher
            .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(request.Token, request.NewPassword),
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
}
