namespace Ligature.Platform.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}