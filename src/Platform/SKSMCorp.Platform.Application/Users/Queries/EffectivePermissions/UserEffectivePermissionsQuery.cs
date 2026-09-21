using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Users.Queries.EffectivePermissions;

/// <summary>
/// USR-Q3 — GetUserAccessSummary (docs/requirements.md, "USR-Q3
/// GetUserAccessSummary"), narrowed by UA3 to the flattened effective
/// permission set.
///
/// NAMED FOR WHAT IT RETURNS. After the narrowing, "access summary" would
/// overstate a response that carries no roles and no dates; AUT-Q2 remains the
/// authoritative assignment read for a user.
///
/// No asOf (UA5): the catalogue says "currently" and names only UserId.
/// Historical effective access belongs to REV-Q6.
/// </summary>
public sealed record UserEffectivePermissionsQuery(UserId UserId)
    : IQuery<UserEffectivePermissionsResult>, IQueryAuthorizationDeclaration
{
    /// <summary>
    /// BOTH, and it is AND (UA2) — a recorded amendment to the catalogue,
    /// which gives this query user.read alone.
    ///
    /// The USR-Q1 composition amendment already established that role
    /// assignments live behind role.read, and DV-7 and DV-8 are live tests
    /// protecting it. This read exposes the permissions those assignments
    /// produce, so serving it to a user.read-only caller would be an alternate
    /// path around that boundary.
    /// </summary>
    public static QueryAuthorization Authorization =>
        QueryAuthorization.Required("user.read", "role.read");
}
