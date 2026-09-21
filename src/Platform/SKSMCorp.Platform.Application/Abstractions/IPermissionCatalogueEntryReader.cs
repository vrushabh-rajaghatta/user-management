using SKSMCorp.Platform.Application.Roles.Queries.WhoCanDo;

namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// One catalogue entry by its code, or null when no such code exists.
///
/// Narrow on purpose. AUT-Q7 needs to tell "no such permission" (404) from
/// "retired, so nobody holds it" and from "nobody holds it" (RW8, RW9), and
/// those three answers cannot be distinguished from an empty holder list
/// alone.
/// </summary>
public interface IPermissionCatalogueEntryReader
{
    Task<RequestedPermission?> FindAsync(
        string permissionCode,
        CancellationToken cancellationToken);
}
