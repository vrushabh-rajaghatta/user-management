using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface ICredentialRepository
{
    Task AddAsync(Credential credential, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the credential for an identity, or null when none exists (SES-C1
    /// step 3).
    ///
    /// Null is the pending-activation state (inv. 15), not an error: absence of
    /// a credential is precisely what prevents authentication, and there is no
    /// status column that could disagree with it.
    ///
    /// TRACKED, because the caller mutates it — failed-attempt counters, lock,
    /// unlock and rehash all write through the change tracker so UnitOfWork can
    /// commit them with the rest of the operation.
    /// </summary>
    Task<Credential?> FindByIdentityAsync(
        UserIdentityId userIdentityId,
        CancellationToken cancellationToken);
}
