using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Domain.Provenance;

public sealed record CreationStamp(
    DateTimeOffset At,
    UserId By);