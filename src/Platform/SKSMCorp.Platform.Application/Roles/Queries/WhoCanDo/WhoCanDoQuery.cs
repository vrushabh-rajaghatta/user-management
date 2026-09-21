using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Roles.Queries.WhoCanDo;

/// <summary>
/// AUT-Q7 — WhoCanDo (docs/requirements.md, "AUT-Q7 WhoCanDo").
///
/// The reverse lookup: not "may this actor do this?" but "who could do this,
/// and why?", at an instant. The catalogue's example is the inspection
/// question it exists to answer — "Who could approve submissions for Product X
/// in March?"
/// </summary>
/// <param name="AsOf">
/// The evaluation instant for every authorisation fact the model DATES (RW3),
/// defaulting to now. It does not reach the permission catalogue's IsActive or
/// user and identity status, which the schema does not represent historically.
/// </param>
/// <param name="ScopeType">
/// Kept because the catalogue names it, and refused unless Global (RW7).
/// </param>
public sealed record WhoCanDoQuery(
    string PermissionCode,
    DateTimeOffset? AsOf,
    string? ScopeType,
    Guid? ScopeId)
    : IQuery<WhoCanDoResult>, IQueryAuthorizationDeclaration
{
    /// <summary>
    /// BOTH, and it is AND (RW6, on RH9's precedent). This answers a question
    /// about people, so role administration alone is not enough to ask it; and
    /// no third code is introduced, because the model already distinguishes
    /// role administration from user visibility.
    /// </summary>
    public static QueryAuthorization Authorization =>
        QueryAuthorization.Required("role.read", "user.read");
}
