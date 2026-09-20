using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Queries.RolePermissions;

// Compile-only stub for the red tests of "Role administration read"
// (docs/requirements.md). Not yet implemented: every role looks empty.
public sealed class RolePermissionsQueryHandler
    : IQueryHandler<RolePermissionsQuery, RolePermissionsResult>
{
    public Task<RolePermissionsResult> Handle(RolePermissionsQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new RolePermissionsResult([]));
}
