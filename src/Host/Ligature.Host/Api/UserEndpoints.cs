using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.CreateUser;

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
                + "THE CREATED ACCOUNT CANNOT YET BE ACTIVATED. USR-C1 issues "
                + "an activation token and stores only its hash; the plaintext "
                + "is discarded, and no delivery mechanism exists yet. This "
                + "endpoint deliberately does not return it — see the "
                + "Notifications gap in docs/requirements.md.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
    }

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
