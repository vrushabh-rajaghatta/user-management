using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.GrantableRoles;

/// <summary>
/// The active roles AUT-C1 can grant. RED STUB. Deliberately NOT AUT-Q5 and
/// given no ID of its own (docs/requirements.md, "AUT-Q2").
/// </summary>
public sealed record GrantableRolesQuery
    : IQuery<GrantableRolesResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization
        => throw new NotImplementedException("The grantable-role list is not implemented.");
}
