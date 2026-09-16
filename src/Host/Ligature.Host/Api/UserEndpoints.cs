using Ligature.Host.Configuration;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.AdminResetPassword;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Commands.RevokeUserSessions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Host.Api;

/// <summary>
/// USR-C1 over HTTP.
///
/// The first AUTHORIZED endpoint. Sign-out proved that a caller can be
/// established and that an unestablished one is refused; this proves the next
/// thing along — that an established caller lacking a catalogue permission is
/// refused too. The endpoint itself does none of that work: it declares
/// nothing about permissions, and AuthorizationBehavior reads
/// CreateUserCommand.RequiredPermission ("user.create") inside the pipeline.
///
/// AN AUTHORIZATION FAILURE IS CURRENTLY A 400, not a 403. The pipeline raises
/// BusinessRuleViolationException for it, which is the same type a duplicate
/// email raises, so the Host cannot tell the two apart — and must not try, by
/// matching on a message or by evaluating the permission itself. Separating
/// them needs a decision about error classification that is larger than this
/// endpoint (docs/requirements.md, "Authorization failures are not
/// distinguishable from validation failures").
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/users", CreateAsync)
            .WithTags("Users")
            .WithSummary("Create a human user account.")
            .WithDescription(
                "Requires a carrier and the 'user.create' permission. A caller "
                + "without it receives 400, not 403 — the pipeline raises the "
                + "same exception type as a validation failure, and the host "
                + "does not guess which occurred.\n\n"
                + "THE ACTIVATION TOKEN IS NOT RETURNED, deliberately. USR-C1 "
                + "issues one and stores only its hash; the plaintext is handed "
                + "to Notifications, which emails the activation link. Returning "
                + "it here would put it in a response body, a proxy log and a "
                + "client's memory — the exact disclosure that storing only a "
                + "hash prevents.\n\n"
                + "A host with no mail configuration delivers nothing: the "
                + "notification is swept to Abandoned, and the account stays "
                + "unactivated. That is the default in a development "
                + "environment.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)

            // Documentation only. AuthorizationBehavior is what refuses a
            // caller without 'user.create'; this marker refuses nothing.
            .WithMetadata(new RequiresCarrier());

        routes.MapPost("/api/users/{userId:guid}/password-reset", ResetPasswordAsync)
            .WithTags("Users")
            .WithSummary("Send a user a password reset link (administrator).")
            .WithDescription(
                "Requires a carrier, the 'user.resetpassword' permission and a "
                + "reason. Issues a reset token and mails the link to the user's "
                + "own address; any earlier reset link stops working. The "
                + "response is 202 with NO body: the administrator never "
                + "receives the token or the password, and the mail is the only "
                + "delivery. A refusal, including a missing permission or an "
                + "ineligible user, is 400.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());

        // SES-C4, administrator form. No way to keep a session: it is not the
        // administrator's to keep (D4).
        routes.MapPost("/api/users/{userId:guid}/sign-out-everywhere", SignOutEverywhereAsync)
            .WithTags("Users")
            .WithSummary("End every active session of a user (administrator).")
            .WithDescription(
                "Requires a carrier, the 'session.revoke' permission and a "
                + "reason. Ends every active session of every identity the user "
                + "holds — including the caller's own current session if they "
                + "target themselves. A user with no active sessions is left as "
                + "they are. Success is 204 with no body. An unknown user, a "
                + "missing permission or a missing reason is 400.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());
    }

    /// <summary>
    /// SES-C4, administrator form, over HTTP. A missing reason is a binding
    /// failure refused here; a blank one is refused by the command.
    /// </summary>
    private static async Task<IResult> SignOutEverywhereAsync(
        Guid userId,
        UserSessionsRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (request?.Reason is null)
        {
            return Results.BadRequest(
                new { Error = "A reason is required." });
        }

        await dispatcher.SendAsync<RevokeUserSessionsCommand, RevokeUserSessionsResult>(
            new RevokeUserSessionsCommand(new UserId(userId), request.Reason),
            cancellationToken);

        return Results.NoContent();
    }

    private sealed record UserSessionsRequest(string? Reason);

    /// <summary>
    /// CRD-C5 over HTTP.
    ///
    /// The endpoint refuses only a body it cannot bind. A present but blank
    /// reason is dispatched, and the command refuses it — so that rule lives in
    /// one place and holds for every caller of the command, not only this one.
    /// </summary>
    private static async Task<IResult> ResetPasswordAsync(
        Guid userId,
        AdminResetPasswordRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (request?.Reason is null)
        {
            return Results.BadRequest(
                new { Error = "A reason is required." });
        }

        await dispatcher.SendAsync<AdminResetPasswordCommand, AdminResetPasswordResult>(
            new AdminResetPasswordCommand(new UserId(userId), request.Reason),
            cancellationToken);

        // 202, not 200: the mail is sent after the command commits and its
        // outcome is not known here. No body, and in particular no token.
        return Results.Accepted();
    }

    private sealed record AdminResetPasswordRequest(string? Reason);

    private static async Task<IResult> CreateAsync(
        CreateUserRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (request is null
            || request.FirstName is null
            || request.LastName is null
            || request.DisplayName is null
            || request.Email is null
            || request.InitialUsername is null)
        {
            return Results.BadRequest(
                new
                {
                    Error =
                        "A first name, last name, display name, email address "
                        + "and initial username are required.",
                });
        }

        var result = await dispatcher.SendAsync<CreateUserCommand, CreateUserResult>(
            new CreateUserCommand(
                request.FirstName,
                request.LastName,
                request.DisplayName,
                request.Email,
                request.InitialUsername),
            cancellationToken);

        // 201 with a body and NO Location header. Location is worth setting
        // when a canonical retrieval URI exists; there is no read endpoint yet,
        // so pointing at one would be pointing at a 404. Its absence does not
        // make the 201 wrong.
        //
        // The body is exactly CreateUserResult and nothing more. In particular
        // it carries no activation token: returning one here would hand a live
        // credential to whoever created the account, which is the disclosure
        // that storing only a hash exists to prevent (UT7) — and would let an
        // administrator set another person's password, making every document
        // that person later approved contestable.
        return Results.Json(
            new
            {
                UserId = result.UserId.Value,
                UserIdentityId = result.UserIdentityId.Value,
            },
            statusCode: StatusCodes.Status201Created);
    }

    private sealed record CreateUserRequest(
        string? FirstName,
        string? LastName,
        string? DisplayName,
        string? Email,
        string? InitialUsername);
}
