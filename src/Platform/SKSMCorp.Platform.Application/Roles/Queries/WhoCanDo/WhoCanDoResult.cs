using SKSMCorp.Platform.Application.Abstractions;

namespace SKSMCorp.Platform.Application.Roles.Queries.WhoCanDo;

/// <summary>
/// AUT-Q7's answer. Holders is null exactly when the permission code does not
/// exist, which the route answers as 404 (RW8).
///
/// NO HOLDER COUNT, deliberately (RW5): a row IS a holder here, so the list's
/// length is the count, and a second number could only ever drift from it.
/// AUT-Q4 needed one because its rows were assignments.
/// </summary>
/// <param name="Permission">
/// Echoed INCLUDING IsActive, which is what makes RW9 legible: an empty list
/// beside IsActive false means "retired, so nobody", and beside IsActive true
/// means "nobody holds it". Without it those two answers are indistinguishable.
/// </param>
public sealed record WhoCanDoResult(
    DateTimeOffset AsOf,
    RequestedPermission? Permission,
    IReadOnlyList<PermissionHolder>? Holders)
{
    public bool PermissionExists => Holders is not null;
}

/// <summary>The permission that was asked about, as the catalogue holds it now.</summary>
public sealed record RequestedPermission(
    Guid PermissionId,
    string Code,
    string Name,
    bool IsActive);
