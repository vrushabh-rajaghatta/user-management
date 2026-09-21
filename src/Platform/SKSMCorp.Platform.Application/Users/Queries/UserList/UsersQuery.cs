using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Users.Queries.UserList;

/// <summary>
/// USR-Q2 — the tenant's human users, one page at a time
/// (docs/requirements.md, "USR-Q2 — User List Query").
///
/// Both parameters are optional and arrive as the caller sent them: null means
/// omitted, and the handler applies the defaults. Range is not checked here —
/// a query type is a declaration, and the bound must hold for every caller of
/// the handler, not only for those that construct the query correctly.
/// </summary>
public sealed record UsersQuery(int? Page, int? PageSize)
    : IQuery<UsersResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization =>
        QueryAuthorization.Required("user.read");
}
