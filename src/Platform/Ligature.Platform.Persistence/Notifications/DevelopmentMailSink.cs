#if DEBUG
using Ligature.Platform.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Ligature.Platform.Persistence.Notifications;

/// <summary>RED STUB (docs/architecture.md §8).</summary>
internal sealed class DevelopmentMailSink : INotificationTransport
{
    public DevelopmentMailSink(string directory, ILogger<DevelopmentMailSink> logger)
    {
    }

    public Task<string?> SendAsync(RenderedMessage message, CancellationToken cancellationToken)
        => throw new NotImplementedException("RED STUB: the development mail sink is not implemented.");
}
#endif
