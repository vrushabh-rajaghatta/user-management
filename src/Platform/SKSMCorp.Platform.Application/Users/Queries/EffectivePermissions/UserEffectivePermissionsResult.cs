using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Queries.EffectivePermissions;

/// <summary>
/// USR-Q3's answer: what this person can do right now.
///
/// NO ROLES, under any circumstances (UA3). The roles half is AUT-Q2's, and
/// returning it here would make this a second role-assignment read.
/// </summary>
/// <param name="Status">
/// The user's CURRENT status (UA4), and what makes an empty set legible: an
/// Inactive user with nothing is a different fact from an Active user with
/// nothing. No empty-reason taxonomy is invented, because the resolver has
/// several reasons for an empty set and turning them into a classification
/// would be a second contract to keep true.
///
/// Current, never historical — app_user.status is tied to deactivated_at by a
/// CHECK that nulls the timestamp on reactivation, so there is no history to
/// reconstruct (the limitation RW4 recorded).
/// </param>
public sealed record UserEffectivePermissionsResult(
    UserId UserId,
    UserStatus Status,
    IReadOnlyList<EffectivePermission> Permissions);
