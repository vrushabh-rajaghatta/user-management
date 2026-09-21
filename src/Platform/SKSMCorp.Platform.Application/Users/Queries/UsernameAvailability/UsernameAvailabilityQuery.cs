using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Users.Queries.UsernameAvailability;

/// <summary>
/// IDN-Q3 CheckUsernameAvailable (docs/requirements.md, "IDN-Q3
/// CheckUsernameAvailable on Create user"), under identity.read. The username
/// is checked exactly as given — the value USR-C1 would receive.
/// </summary>
public sealed record UsernameAvailabilityQuery(string Username)
    : IQuery<UsernameAvailabilityResult>, IQueryAuthorizationDeclaration
{
    public static QueryAuthorization Authorization => QueryAuthorization.Required("identity.read");
}
