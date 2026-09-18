using Ligature.Host.Configuration;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.GrantRole;
using Ligature.Platform.Application.Users.Commands.RevokeRole;
using Ligature.Platform.Domain.Users;

namespace Ligature.Host.Api;

/// <summary>
/// AUT-C1 and AUT-C2 over HTTP (docs/requirements.md, "Role Assignment").
///
/// Authorisation is the pipeline's: a caller without role.grant or
/// role.revoke is refused inside it, with the same 400 a validation failure
/// gets. A missing required field is refused here, where the request is bound,
/// as on the existing endpoints.
/// </summary>
public static class RoleAssignmentEndpoints
{
    public static void MapRoleAssignmentEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/users/{userId:guid}/role-assignments", GrantAsync)
            .WithTags("Role assignments")
            .WithSummary("Grant a user a role (administrator).")
            .WithDescription(
                "Requires a carrier, the 'role.grant' permission and a reason. "
                + "Global scope only. 'effectiveFrom' is optional and defaults "
                + "to now; it may not be in the past. 'effectiveTo' is optional "
                + "(open-ended when omitted) and must be strictly after "
                + "'effectiveFrom'. Success is 201 with exactly "
                + "{ userRoleAssignmentId } and no Location header: no "
                + "single-assignment read exists. An overlapping assignment of "
                + "the same role, a retired or unknown role, a target that is "
                + "not an active human, and a missing permission are 400.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());

        routes.MapPost("/api/role-assignments/{assignmentId:guid}/revoke", RevokeAsync)
            .WithTags("Role assignments")
            .WithSummary("End a role assignment (administrator).")
            .WithDescription(
                "Requires a carrier, the 'role.revoke' permission and a reason. "
                + "An active assignment ends now; a future one is closed at its "
                + "own start, so it never takes effect. Success is 204 with no "
                + "body. An unknown, already-revoked or already-ended "
                + "assignment, and a missing permission, are 400.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());
    }

    private static async Task<IResult> GrantAsync(
        Guid userId,
        GrantRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (request?.RoleId is null)
            return Results.BadRequest(new { Error = "A role is required." });

        if (request.Reason is null)
            return Results.BadRequest(new { Error = "A reason is required." });

        var result = await dispatcher.SendAsync<GrantRoleCommand, GrantRoleResult>(
            new GrantRoleCommand(
                new UserId(userId),
                new RoleId(request.RoleId.Value),
                request.EffectiveFrom,
                request.EffectiveTo,
                request.Reason),
            cancellationToken);

        // 201 with the identifier and deliberately NO Location, as POST
        // /api/users has none: there is no single-assignment read to point at.
        return Results.Json(
            new { UserRoleAssignmentId = result.UserRoleAssignmentId.Value },
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> RevokeAsync(
        Guid assignmentId,
        RevokeRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (request?.Reason is null)
            return Results.BadRequest(new { Error = "A reason is required." });

        await dispatcher.SendAsync<RevokeRoleCommand, RevokeRoleResult>(
            new RevokeRoleCommand(new UserRoleId(assignmentId), request.Reason),
            cancellationToken);

        return Results.NoContent();
    }

    private sealed record GrantRequest(Guid? RoleId, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo, string? Reason);

    private sealed record RevokeRequest(string? Reason);
}
