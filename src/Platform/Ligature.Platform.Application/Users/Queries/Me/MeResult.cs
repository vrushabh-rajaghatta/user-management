using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Queries.Me;

public sealed record MeResult(
    MeIdentity Identity,
    IReadOnlyList<EffectivePermission> Permissions,
    MeSession Session);

public sealed record MeIdentity(
    UserIdentityId UserIdentityId,
    string Username,
    string DisplayName);

/// <param name="IdleExpiresAt">
/// Informational and CONSERVATIVE, never an authoritative deadline. Activity
/// writes are throttled and enforcement carries a documented tolerance, so the
/// server may still accept a request after this instant. A client must not run
/// a countdown from it or sign anyone out on it (D6).
/// </param>
public sealed record MeSession(
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt);
