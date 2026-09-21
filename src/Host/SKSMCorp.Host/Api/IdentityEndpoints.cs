using SKSMCorp.Host.Configuration;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Users.Commands.UnlockAccount;
using SKSMCorp.Platform.Application.Users.Queries.UserIdentities;
using SKSMCorp.Platform.Application.Users.Queries.UsernameAvailability;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Host.Api;

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

        // IDN-Q1 GetUserIdentities, as amended: the user's identities with the
        // lock state judged at read time. The lock state is a projection,
        // never stored; the subject id and the failure count are not served.
        routes.MapGet("/api/users/{userId:guid}/identities", ListAsync)
            .WithTags("Identities")
            .WithSummary("A user's sign-in identities, with their lock state.")
            .WithDescription(
                "Requires a carrier and the 'identity.read' permission. Returns "
                + "{ identities }, oldest first, each with userIdentityId, type "
                + "('Local' or 'External'), provider, username (null for "
                + "external), status, deactivatedAt, locked and lockedUntil. "
                + "'locked' is decided by the server at the moment of the read: "
                + "true only while a lock is in force, when 'lockedUntil' is its "
                + "end; otherwise false and null. Human users only: an unknown "
                + "user, the System actor and a missing permission are 400. Not "
                + "audited.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());

        // IDN-Q3 CheckUsernameAvailable. A read, but POST: the typed username
        // travels in the body so it never reaches a request URL — access logs,
        // proxies, browser history (UN1).
        routes.MapPost("/api/identities/username-availability", UsernameAvailabilityAsync)
            .WithTags("Identities")
            .WithSummary("Whether a username is free for a new local identity.")
            .WithDescription(
                "Requires a carrier and the 'identity.read' permission. Takes "
                + "{ username } and returns { available }: false when a local "
                + "identity of any status holds it, in any case; availability "
                + "is absolute. The same check USR-C1 makes, so 'available' "
                + "means USR-C1 would accept it now — not that it will at "
                + "submit. Nothing about the holder is returned. A blank "
                + "username and a missing permission are 400. Not audited.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());

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
    private static async Task<IResult> ListAsync(
        Guid userId,
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync<UserIdentitiesQuery, UserIdentitiesResult>(
            new UserIdentitiesQuery(new UserId(userId)),
            cancellationToken);

        return Results.Ok(new
        {
            Identities = result.Identities.Select(x => new
            {
                UserIdentityId = x.UserIdentityId.Value,
                Type = x.Type.ToString(),
                x.Provider,
                x.Username,
                Status = x.Status.ToString(),
                x.DeactivatedAt,
                x.Locked,
                x.LockedUntil,
            }),
        });
    }

    private static async Task<IResult> UsernameAvailabilityAsync(
        UsernameAvailabilityRequest? request,
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        // A missing body or field is the blank case, refused by the query.
        var result = await dispatcher.SendAsync<UsernameAvailabilityQuery, UsernameAvailabilityResult>(
            new UsernameAvailabilityQuery(request?.Username ?? string.Empty),
            cancellationToken);

        return Results.Ok(new { result.Available });
    }

    /// <summary>The username as typed; never logged, never echoed back.</summary>
    private sealed record UsernameAvailabilityRequest(string? Username);

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
