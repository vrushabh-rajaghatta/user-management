using Ligature.Host.Configuration;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Roles.Commands.CreateRole;
using Ligature.Platform.Application.Roles.Commands.DeactivateRole;
using Ligature.Platform.Application.Roles.Commands.ReactivateRole;
using Ligature.Platform.Application.Roles.Commands.UpdateRoleMetadata;
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

        // AUT-C3. POST beside the grant form's GET on the same path: a
        // different verb, a different question, and neither disturbs the other.
        routes.MapPost("/api/roles", CreateAsync)
            .WithTags("Roles")
            .WithSummary("Create a role the tenant owns (administrator).")
            .WithDescription(
                "Requires a carrier and the 'role.manage' permission, and a human "
                + "caller. Body: code and name, both required, and description, "
                + "optional. The code is stored exactly as supplied and must not "
                + "begin or end with whitespace; a code that matches an existing "
                + "one, in any case, is refused. The name and description are "
                + "trimmed, must be free of control characters and at most 100 "
                + "characters, and the trimmed values are stored. The role is "
                + "created active, owned by the tenant, with no permissions and no "
                + "holders. Success is 201 with the created role. An invalid input, "
                + "a code already in use and a missing permission are 400.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());

        // AUT-C4. POST /{id}/{noun}, the convention every user mutation uses
        // (RM6); this host has no PUT or PATCH.
        routes.MapPost("/api/roles/{roleId:guid}/metadata", UpdateMetadataAsync)
            .WithTags("Roles")
            .WithSummary("Change a tenant role's name and description (administrator).")
            .WithDescription(
                "Requires a carrier and the 'role.manage' permission, and a human "
                + "caller. Body: name, required, and description, optional. Both "
                + "are trimmed, must be free of control characters and at most 100 "
                + "characters, and the trimmed values are stored; a description "
                + "that is absent or blank is stored as none. Names need not be "
                + "unique. The code and the role's ownership are not inputs and do "
                + "not change. A release-owned role is refused. Success is 200 with "
                + "the role AS STORED, whether or not this call changed it; an edit "
                + "that changes nothing writes nothing and records nothing. An "
                + "invalid input, an unknown role, a release-owned role and a "
                + "missing permission are 400.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());

        // AUT-C5 / AUT-C6. The state-changing pair, in the same convention.
        routes.MapPost("/api/roles/{roleId:guid}/deactivate", DeactivateAsync)
            .WithTags("Roles")
            .WithSummary("Retire a tenant role (administrator).")
            .WithDescription(
                "Requires a carrier and the 'role.manage' permission, and a human "
                + "caller. Body: reason, required and not blank. Retiring a role "
                + "PREVENTS NEW ASSIGNMENTS; it does not revoke any existing one, "
                + "and every current holder keeps the access the role grants. The "
                + "role is never deleted. A role that is already inactive answers "
                + "200 and writes nothing. A release-owned role is refused. Success "
                + "is 200 with the role as stored. A missing or blank reason, an "
                + "unknown role, a release-owned role and a missing permission "
                + "are 400.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());

        routes.MapPost("/api/roles/{roleId:guid}/reactivate", ReactivateAsync)
            .WithTags("Roles")
            .WithSummary("Return a retired tenant role to use (administrator).")
            .WithDescription(
                "Requires a carrier and the 'role.manage' permission, and a human "
                + "caller. No body, and no reason: the catalogue gives this command "
                + "none. The role may be assigned again; existing holders were never "
                + "affected. A role that is already active answers 200 and writes "
                + "nothing. A release-owned role is refused. Success is 200 with the "
                + "role as stored.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithMetadata(new RequiresCarrier());

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

    private static async Task<IResult> CreateAsync(
        CreateRoleRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        // Missing inputs are refused where the request is bound, as on the
        // other endpoints; everything else about them is the domain's.
        if (request?.Code is null || request.Name is null)
            return Results.BadRequest(new { Error = "code and name are required." });

        var result = await dispatcher.SendAsync<CreateRoleCommand, CreateRoleResult>(
            new CreateRoleCommand(request.Code, request.Name, request.Description),
            cancellationToken);

        // 201 with the created role and NO Location header: AUT-Q5 lists roles
        // and AUT-Q3 reads one role's permissions, but there is no GetRole to
        // point at, and a Location that 404s is worse than none.
        return Results.Created((string?)null, new
        {
            RoleId = result.RoleId.Value,
            result.Code,
            result.Name,
            result.Description,
            result.IsSystemRole,
            result.IsActive,
        });
    }

    private sealed record CreateRoleRequest(string? Code, string? Name, string? Description);

    private static async Task<IResult> UpdateMetadataAsync(
        Guid roleId,
        UpdateRoleMetadataRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (request?.Name is null)
            return Results.BadRequest(new { Error = "name is required." });

        var result = await dispatcher.SendAsync<UpdateRoleMetadataCommand, UpdateRoleMetadataResult>(
            new UpdateRoleMetadataCommand(new RoleId(roleId), request.Name, request.Description),
            cancellationToken);

        // The role AS STORED (RM6), so the caller learns the normalised values
        // without normalising anything itself.
        return Results.Ok(new
        {
            RoleId = result.RoleId.Value,
            result.Code,
            result.Name,
            result.Description,
            result.IsSystemRole,
            result.IsActive,
        });
    }

    /// <summary>
    /// Name and description only. A code or an ownership flag in the payload
    /// binds to nothing and changes nothing (RM2, RM10): the command has no
    /// such input, so there is no attempted mutation to refuse.
    /// </summary>
    private sealed record UpdateRoleMetadataRequest(string? Name, string? Description);

    private static async Task<IResult> DeactivateAsync(
        Guid roleId,
        DeactivateRoleRequest? request,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (request?.Reason is null)
            return Results.BadRequest(new { Error = "A reason is required." });

        var result = await dispatcher.SendAsync<DeactivateRoleCommand, DeactivateRoleResult>(
            new DeactivateRoleCommand(new RoleId(roleId), request.Reason),
            cancellationToken);

        return Stored(result.RoleId, result.Code, result.Name, result.Description, result.IsSystemRole, result.IsActive);
    }

    private static async Task<IResult> ReactivateAsync(
        Guid roleId,
        ICommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync<ReactivateRoleCommand, ReactivateRoleResult>(
            new ReactivateRoleCommand(new RoleId(roleId)),
            cancellationToken);

        return Stored(result.RoleId, result.Code, result.Name, result.Description, result.IsSystemRole, result.IsActive);
    }

    /// <summary>The role AS STORED (RD6), the same six members AUT-C3 and AUT-C4 answer.</summary>
    private static IResult Stored(
        RoleId roleId, string code, string name, string? description, bool isSystemRole, bool isActive)
        => Results.Ok(new
        {
            RoleId = roleId.Value,
            Code = code,
            Name = name,
            Description = description,
            IsSystemRole = isSystemRole,
            IsActive = isActive,
        });

    /// <summary>A reason, and nothing else. AUT-C6 has no request body at all (RD5).</summary>
    private sealed record DeactivateRoleRequest(string? Reason);

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
