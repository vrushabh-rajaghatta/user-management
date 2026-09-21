namespace SKSMCorp.Platform.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}