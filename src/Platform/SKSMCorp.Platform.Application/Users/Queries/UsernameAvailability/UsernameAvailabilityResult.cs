namespace SKSMCorp.Platform.Application.Users.Queries.UsernameAvailability;

/// <summary>IDN-Q3's answer, and nothing else: never who holds it, nor their status.</summary>
public sealed record UsernameAvailabilityResult(bool Available);
