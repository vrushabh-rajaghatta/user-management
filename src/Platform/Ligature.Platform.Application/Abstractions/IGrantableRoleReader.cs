using Ligature.Platform.Application.Users.Queries.GrantableRoles;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>RED STUB.</summary>
public interface IGrantableRoleReader
{
    /// <summary>The active roles, in the contract's order.</summary>
    Task<IReadOnlyList<GrantableRole>> ReadAsync(CancellationToken cancellationToken);
}
