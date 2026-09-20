using Ligature.Host.Configuration;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Roles.Queries.PermissionCatalogue;
using Ligature.Platform.Application.Roles.Queries.RoleAdministration;
using Ligature.Platform.Application.Roles.Queries.RolePermissions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Host.Api;

/// <summary>
/// The role administration reads over HTTP (docs/requirements.md, "Role
/// administration read"): AUT-Q5, AUT-Q3 and AUT-Q6, all under role.read.
///
/// SEPARATE FROM /api/roles (RA6), which stays exactly as it was: the narrow
/// grantable-role list the assignment form uses. This is the administration
/// surface, and it answers different questions.
///
/// AUT-Q3 answers 404 for a role that does not exist (RA11), which is the only
/// 404 with a body in this host. It is produced HERE, from the query's own
/// result: ProblemMiddleware maps refusals to 400, 401, 429 and 500, and this
/// story deliberately does not widen that allowlist.
/// </summary>
public static class RoleEndpoints
{
    public static void MapRoleEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // AUT-Q5.
        routes.MapGet("/api/roles/administration", ListAsync)
            .WithTags("Roles")
            .WithSummary("The role administration list.")
            .WithDescription(
                "Requires a carrier and the 'role.read' permission. Returns "
                + "{ roles }, each exactly { roleId, code, name, description, "
                + "isSystemRole, isActive, agentAssignable, permissionCount, "
                + "activeHolderCount }, ordered by name. 'includeInactive' adds "
                + "inactive roles; 'agentAssignableOnly' keeps only roles no live "
                + "human-only permission is granted to. Both narrow the result "
                + "set only: the counts and agentAssignable are unchanged by "
                + "either. permissionCount counts live grants whatever the "
                + "catalogue says about the permission; activeHolderCount counts "
                + "distinct holders whose assignment is active now. Not the "
                + "grantable-role list at GET /api/roles.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());

        // AUT-Q3.
        routes.MapGet("/api/roles/{roleId:guid}/permissions", PermissionsAsync)
            .WithTags("Roles")
            .WithSummary("What a role carries, now or at an instant.")
            .WithDescription(
                "Requires a carrier and the 'role.read' permission. Returns "
                + "{ permissions }, each exactly { rolePermissionId, permissionId, "
                + "code, name, resource, action, requiresHumanActor, grantedAt, "
                + "revokedAt }, ordered by code: the grants live at 'asOf', which "
                + "defaults to now. A role with no live grants is 200 and an empty "
                + "list; a role that does not exist is 404.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithMetadata(new RequiresCarrier());

        // AUT-Q6.
        routes.MapGet("/api/permissions", CatalogueAsync)
            .WithTags("Roles")
            .WithSummary("The permission catalogue, read-only.")
            .WithDescription(
                "Requires a carrier and the 'role.read' permission. Returns "
                + "{ permissions }, each exactly { permissionId, code, name, "
                + "resource, action, requiresHumanActor, isActive }, ordered by "
                + "code, retired entries included. 'resource' and "
                + "'requiresHumanActor' filter it. The catalogue is owned by the "
                + "release: there is no command that changes it.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (!TryReadFlag(request, "includeInactive", out var includeInactive, out var refusal)
            || !TryReadFlag(request, "agentAssignableOnly", out var agentAssignableOnly, out refusal))
        {
            return Results.BadRequest(new { Error = refusal });
        }

        var result = await dispatcher.SendAsync<RoleAdministrationQuery, RoleAdministrationResult>(
            new RoleAdministrationQuery(includeInactive, agentAssignableOnly),
            cancellationToken);

        return Results.Ok(new
        {
            Roles = result.Roles.Select(x => new
            {
                x.RoleId,
                x.Code,
                x.Name,
                x.Description,
                x.IsSystemRole,
                x.IsActive,
                x.AgentAssignable,
                x.PermissionCount,
                x.ActiveHolderCount,
            }),
        });
    }

    private static async Task<IResult> PermissionsAsync(
        Guid roleId,
        HttpRequest request,
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var values = request.Query["asOf"];
        DateTimeOffset? asOf = null;

        if (values.Count == 1
            && DateTimeOffset.TryParse(
                values[0],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            asOf = parsed;
        }
        else if (values.Count != 0)
        {
            return Results.BadRequest(new { Error = "asOf must be one ISO-8601 instant." });
        }

        var result = await dispatcher.SendAsync<RolePermissionsQuery, RolePermissionsResult>(
            new RolePermissionsQuery(new RoleId(roleId), asOf),
            cancellationToken);

        // RA11. The body is what tells this apart from a route that does not
        // exist, which answers 404 with nothing.
        if (!result.RoleExists)
            return Results.NotFound(new { Error = "The role does not exist." });

        return Results.Ok(new
        {
            Permissions = result.Permissions!.Select(x => new
            {
                x.RolePermissionId,
                x.PermissionId,
                x.Code,
                x.Name,
                x.Resource,
                x.Action,
                x.RequiresHumanActor,
                x.GrantedAt,
                x.RevokedAt,
            }),
        });
    }

    private static async Task<IResult> CatalogueAsync(
        HttpRequest request,
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var resources = request.Query["resource"];

        if (resources.Count > 1)
            return Results.BadRequest(new { Error = "resource must be supplied once." });

        bool? requiresHumanActor = null;
        var flags = request.Query["requiresHumanActor"];

        if (flags.Count == 1 && flags[0] is "true" or "false")
        {
            requiresHumanActor = flags[0] == "true";
        }
        else if (flags.Count != 0)
        {
            return Results.BadRequest(new { Error = "requiresHumanActor must be true or false, supplied once." });
        }

        var result = await dispatcher.SendAsync<PermissionCatalogueQuery, PermissionCatalogueResult>(
            new PermissionCatalogueQuery(resources.Count == 1 ? resources[0] : null, requiresHumanActor),
            cancellationToken);

        return Results.Ok(new
        {
            Permissions = result.Permissions.Select(x => new
            {
                x.PermissionId,
                x.Code,
                x.Name,
                x.Resource,
                x.Action,
                x.RequiresHumanActor,
                x.IsActive,
            }),
        });
    }

    /// <summary>
    /// A boolean query parameter, as the assignments route reads
    /// 'includeInactive': absent is false, and anything but one 'true' or
    /// 'false' is refused rather than guessed at.
    /// </summary>
    private static bool TryReadFlag(HttpRequest request, string name, out bool value, out string? refusal)
    {
        var values = request.Query[name];

        if (values.Count == 0)
        {
            value = false;
            refusal = null;

            return true;
        }

        if (values.Count == 1 && values[0] is "true" or "false")
        {
            value = values[0] == "true";
            refusal = null;

            return true;
        }

        value = false;
        refusal = $"{name} must be true or false, supplied once.";

        return false;
    }
}
