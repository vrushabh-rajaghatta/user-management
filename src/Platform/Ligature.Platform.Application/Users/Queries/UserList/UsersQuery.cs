using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.UserList;

/// <summary>
/// USR-Q1 — the user list. RED STUB: the contract is in docs/requirements.md,
/// and this type exists only so the tests that describe it compile.
/// </summary>
public sealed record UsersQuery(int? Page, int? PageSize)
    : IQuery<UsersResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization =>
        throw new NotImplementedException("USR-Q1 is not implemented.");
}
