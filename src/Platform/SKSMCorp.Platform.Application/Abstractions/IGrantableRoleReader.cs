using SKSMCorp.Platform.Application.Users.Queries.GrantableRoles;

namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>The grantable-role list's read. Not AUT-Q5.</summary>
public interface IGrantableRoleReader
{
    /// <summary>The active roles, ordered by name under ICU "unicode", then id.</summary>
    Task<IReadOnlyList<GrantableRole>> ReadAsync(CancellationToken cancellationToken);
}
