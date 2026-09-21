using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Users.Queries.GrantableRoles;

/// <summary>
/// The active roles AUT-C1 can grant (docs/requirements.md, "AUT-Q2").
///
/// A Story 2 dependency, deliberately NOT AUT-Q5 and given no ID of its own:
/// no permission or holder counts, no includeInactive, no agent-assignable
/// filter. Those are AUT-Q5's, in Slice 4.
/// </summary>
public sealed record GrantableRolesQuery
    : IQuery<GrantableRolesResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("role.read");
}
