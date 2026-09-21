using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Queries.UserIdentities;

/// <summary>
/// IDN-Q1 GetUserIdentities, as amended (docs/requirements.md, "IDN-Q1
/// GetUserIdentities and Unlock on the User detail page"): every identity of
/// one human user, with the lock state derived at read time, under
/// identity.read.
/// </summary>
public sealed record UserIdentitiesQuery(UserId UserId)
    : IQuery<UserIdentitiesResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("identity.read");
}
