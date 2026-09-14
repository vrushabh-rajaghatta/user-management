using Ligature.Host.Configuration;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.UnlockAccount;
using Ligature.Platform.Domain.Users;

namespace Ligature.Host.Api;

/// <summary>
/// Identity-keyed administration over HTTP, starting with CRD-C6.
///
/// The route follows the command's own key. Lockout lives on the credential of
/// one local identity, so the identity is what is named — nesting it under a
/// user would suggest the command resolves through the user, which it must not.
///
/// As with the user endpoints, authorisation is the pipeline's: a caller
/// without the permission is refused inside it, with the same 400 a validation
/// failure gets (docs/requirements.md).
/// </summary>
public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/identities/{identityId:guid}/unlock", UnlockAsync)
            .WithTags("Identities")
            .WithSummary("Clear a live lockout on an identity (administrator).")
            .WithDescription(
                "Requires a carrier, the 'user.unlock' permission and a reason. "
                + "Clears a lockout that is still in force; an expired lock, or "
                + "an account that is not locked, is refused. An administrator "
                + "cannot unlock their own account. Success is 204 with no "
                + "body. A refusal, including a missing permission or an "
                + "ineligible identity, is 400.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)

            // Documentation only. The pipeline refuses an unauthenticated
            // caller; this marker refuses nothing.
            .WithMetadata(new RequiresCarrier());
    }

    /// <summary>
    /// A missing reason is a binding failure refused here; a present but blank
    /// one is dispatched and refused by the command, so the rule lives in one
    /// place for every caller of the command.
    /// </summary>
    private static async Task<IResult> UnlockAsync(
        Guid identityId,
        UnlockRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (request?.Reason is null)
        {
            return Results.BadRequest(
                new { Error = "A reason is required." });
        }

        await dispatcher.SendAsync<UnlockAccountCommand, UnlockAccountResult>(
            new UnlockAccountCommand(new UserIdentityId(identityId), request.Reason),
            cancellationToken);

        return Results.NoContent();
    }

    private sealed record UnlockRequest(string? Reason);
}
