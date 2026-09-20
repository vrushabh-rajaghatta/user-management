using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// What AUT-C7 needs to know about a permission before granting it, and
/// nothing else (RG7).
///
/// DELIBERATELY NOT AUT-Q6's reader. That one is query infrastructure: it
/// answers the catalogue screen, is keyed by neither id nor lock, and returns
/// a DTO shaped for a table. A command that leaned on it would depend on a
/// read model's shape for a decision it must make itself.
/// </summary>
public interface IPermissionRepository
{
    Task<PermissionFacts?> FindAsync(
        PermissionId permissionId,
        CancellationToken cancellationToken);
}

/// <summary>Existence, activity, and the regulatory flag (RP6). Nothing more.</summary>
public sealed record PermissionFacts(
    PermissionId PermissionId,
    string Code,
    bool IsActive,
    bool RequiresHumanActor);
