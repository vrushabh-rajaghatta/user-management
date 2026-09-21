using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// Everything a caller effectively holds, at an instant.
///
/// It names no permission and no scope: this is the whole set, not a question
/// about one entry. The instant is required for the same reason the single
/// check needs it — an assignment authorises only within its effective period.
/// </summary>
public sealed record EffectivePermissionsRequest(
    UserId UserId,
    DateTimeOffset At);
