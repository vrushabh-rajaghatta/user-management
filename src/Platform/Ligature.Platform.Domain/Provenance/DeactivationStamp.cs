namespace Ligature.Platform.Domain.Users;

public sealed record DeactivationStamp(
    DateTimeOffset At,
    UserId By);