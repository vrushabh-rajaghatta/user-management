using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>Reads roles for the commands that act on them (AUT-C1), and adds the tenant's own (AUT-C3).</summary>
public interface IRoleRepository
{
    Task<Role?> FindAsync(
        RoleId roleId,
        CancellationToken cancellationToken);

    /// <summary>
    /// AUT-C3 (RC1): whether any role already holds this code, IGNORING CASE.
    /// The database's index is case-sensitive and stays the guarantee; this is
    /// what makes a case-only clash a refusal rather than a second role.
    /// </summary>
    Task<bool> ExistsWithCodeAsync(
        string code,
        CancellationToken cancellationToken);

    /// <summary>
    /// AUT-C3 (RC9): adds a role to the change tracker. The unit of work owns
    /// the save. Ownership is not this method's to decide — the domain creates
    /// the role with IsSystemRole false, so this cannot introduce a
    /// release-owned one.
    /// </summary>
    Task AddAsync(
        Role role,
        CancellationToken cancellationToken);
}
