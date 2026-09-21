using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Roles.Queries.RoleMembers;

/// <summary>
/// AUT-Q4 — GetRoleMembers (docs/requirements.md, "AUT-Q4 GetRoleMembers").
///
/// Who holds this role at an instant. NOT a safety check before AUT-C5: the
/// catalogue's note to "call before AUT-C5 to warn about stranding holders"
/// was examined and rejected at that gate (RD2, RD3), because deactivation
/// revokes nobody's access. This read exists for the access-review question.
/// </summary>
/// <param name="AsOf">
/// The instant the whole answer is judged at (RH4), defaulting to now. There
/// is no includeInactive: AUT-Q2 answers "this user's assignments, optionally
/// including inactive ones", where this answers "who holds this role, now".
/// </param>
/// <param name="ScopeId">
/// Kept because the frozen catalogue names it (RH3). V1 has one scope, and
/// GrantRole writes only Global, so a supplied value names a scope that cannot
/// exist and is refused. No scope semantics are introduced here.
/// </param>
public sealed record RoleMembersQuery(RoleId RoleId, DateTimeOffset? AsOf, Guid? ScopeId)
    : IQuery<RoleMembersResult>, IQueryAuthorizationDeclaration
{
    /// <summary>
    /// BOTH, and it is AND (RH9). role.read establishes the authority to
    /// inspect the role; user.read establishes the authority to see the people
    /// in the answer. This is the first read that turns a role into named
    /// people, and without the second code role administration would become an
    /// indirect user directory.
    /// </summary>
    public static QueryAuthorization Authorization =>
        QueryAuthorization.Required("role.read", "user.read");
}
