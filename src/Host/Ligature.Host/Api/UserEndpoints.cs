using System.Globalization;
using Ligature.Host.Configuration;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.AdminResetPassword;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Commands.RevokeUserSessions;
using Ligature.Platform.Application.Users.Queries.UserList;
using Ligature.Platform.Domain.Users;

namespace Ligature.Host.Api;

/// <summary>
/// USR-C1 over HTTP, and the user-scoped administrator routes beside it;
/// USR-Q1, the user list, shares the prefix.
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

        // USR-Q1. Same prefix as the user commands, not the same authorization:
        // this requires user.read and nothing else, and every action a row can
        // start authorizes itself.
        routes.MapGet("/api/users", ListAsync)
            .WithTags("Users")
            .WithSummary("List the tenant's human users, one page at a time.")
            .WithDescription(
                "Requires a carrier and the 'user.read' permission. Each row is "
                + "exactly userId, displayName and email; userId is the only "
                + "identifier, and is what /api/users/{userId}/... accepts. "
                + "email may be null.\n\n"
                + "Optional 'page' (default 1) and 'pageSize' (default 25, "
                + "maximum 100). A value that is not an integer, does not fit "
                + "one, or is supplied twice is 400; so is an out-of-range value, "
                + "which is refused rather than corrected. Unknown parameters are "
                + "ignored. A page past the end is empty with hasMore false.\n\n"
                + "Ordered by displayName (ICU root collation), then userId. There "
                + "is no sorting, filtering or total count, and the list is not a "
                + "snapshot across requests. A caller without 'user.read' "
                + "receives 400, not 403.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
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
    /// USR-Q1 over HTTP.
    ///
    /// THE PARAMETERS ARE READ AS TEXT AND PARSED HERE, not bound as int?.
    /// Framework binding refuses page=abc itself, with an EMPTY-bodied 400 that
    /// ProblemMiddleware never sees — a second error shape for one kind of
    /// failure. Reading the raw values also sees a parameter supplied twice,
    /// which binding would not report as such.
    ///
    /// Only malformed values stop here, where there is no value to hand on. An
    /// integer out of range is the query's refusal, so the bound holds for
    /// every caller of the handler and follows its authorization.
    ///
    /// Anything else in the query string is ignored: no parameter but these two
    /// is read, so none can change the order or narrow the set (P12, P14).
    /// </summary>
    private static async Task<IResult> ListAsync(
        HttpRequest request,
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (!TryReadInteger(request, "page", out var page, out var pageProblem))
            return Results.BadRequest(new { Error = pageProblem });

        if (!TryReadInteger(request, "pageSize", out var pageSize, out var pageSizeProblem))
            return Results.BadRequest(new { Error = pageSizeProblem });

        var result = await dispatcher.SendAsync<UsersQuery, UsersResult>(
            new UsersQuery(page, pageSize),
            cancellationToken);

        return Results.Ok(
            new
            {
                Users = result.Users.Select(x => new
                {
                    UserId = x.UserId.Value,
                    x.DisplayName,
                    x.Email,
                }),
                result.Page,
                result.PageSize,
                result.HasMore,
            });
    }

    /// <summary>
    /// Absent is null and valid. Present means exactly one value that parses as
    /// an int — an optional sign and digits, invariant culture — and nothing
    /// else: not a decimal, not an exponent, not a number too large for int.
    /// </summary>
    private static bool TryReadInteger(
        HttpRequest request,
        string name,
        out int? value,
        out string? problem)
    {
        value = null;
        problem = null;

        if (!request.Query.TryGetValue(name, out var values))
            return true;

        if (values.Count != 1)
        {
            problem = $"'{name}' may be supplied only once.";
            return false;
        }

        if (!int.TryParse(values[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            problem = $"'{name}' must be a whole number.";
            return false;
        }

        value = parsed;
        return true;
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
