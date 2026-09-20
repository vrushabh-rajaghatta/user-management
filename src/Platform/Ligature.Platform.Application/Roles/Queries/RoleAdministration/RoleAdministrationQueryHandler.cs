using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Queries.RoleAdministration;

// Compile-only stub for the red tests of "Role administration read"
// (docs/requirements.md). Not yet implemented: it reads nothing.
public sealed class RoleAdministrationQueryHandler
    : IQueryHandler<RoleAdministrationQuery, RoleAdministrationResult>
{
    public Task<RoleAdministrationResult> Handle(RoleAdministrationQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new RoleAdministrationResult([]));
}
