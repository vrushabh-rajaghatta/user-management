using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.UserProfile;

/// <summary>
/// USR-Q1 GetUser v2 (docs/requirements.md, "USR-Q1 GetUser v2 and the User
/// detail page"): the names, email, status and activationPending. Identities
/// and assignments are other reads, under their own permissions (the USR-Q1
/// composition amendment).
/// </summary>
public sealed record UserProfileQuery(UserId UserId)
    : IQuery<UserProfileResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("user.read");
}
