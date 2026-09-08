using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;

namespace Ligature.Host.Api;

/// <summary>
/// CRD-C1 over HTTP.
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
}
