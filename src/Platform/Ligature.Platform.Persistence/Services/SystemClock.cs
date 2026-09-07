using Ligature.Platform.Application.Abstractions;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// The single source of "now" for everything that persists a timestamp.
/// Handlers and interceptors take <see cref="IClock"/> rather than calling
/// <see cref="DateTimeOffset.UtcNow"/> directly, so that a test can pin time
/// and assert on effective-dating without racing the wall clock.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
