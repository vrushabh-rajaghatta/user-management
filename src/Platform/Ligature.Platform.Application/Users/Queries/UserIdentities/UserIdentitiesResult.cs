using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Queries.UserIdentities;

/// <summary>IDN-Q1's response: the user's identities, oldest first.</summary>
public sealed record UserIdentitiesResult(IReadOnlyList<UserIdentityView> Identities);

/// <summary>
/// One identity as served. Locked and LockedUntil are a READ-TIME PROJECTION
/// of the credential's LockedUntil against the server's clock (CRD-C6's rule:
/// a lock currently in force), not persisted identity state. LockedUntil is
/// set only when Locked. No subject id (deferred) and no failure count.
/// </summary>
public sealed record UserIdentityView(
    UserIdentityId UserIdentityId,
    IdentityType Type,
    string Provider,
    string? Username,
    UserStatus Status,
    DateTimeOffset? DeactivatedAt,
    bool Locked,
    DateTimeOffset? LockedUntil);
