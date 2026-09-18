using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.UserProfile;

/// <summary>
/// USR-Q1 GetUser, narrow v1 (docs/requirements.md, "USR-C2 — Update User
/// Profile, and USR-Q1 GetUser (narrow v1)"): exactly the four profile fields
/// an edit needs, and none of the catalogue's other GetUser fields.
/// </summary>
public sealed record UserProfileQuery(UserId UserId)
    : IQuery<UserProfileResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("user.read");
}
