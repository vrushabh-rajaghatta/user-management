#if DEBUG
using System.Text;
using Ligature.Platform.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Ligature.Platform.Persistence.Notifications;

/// <summary>
/// DEVELOPMENT ONLY (docs/architecture.md §8, "development mail sink"). Writes
/// each rendered message to a file instead of sending it, so a developer with no
/// Google Workspace account can still follow an activation or reset link.
///
/// COMPILED ONLY INTO DEBUG BUILDS. The production image publishes Release, so
/// this type is not in its assemblies at all; the host also refuses the sink's
/// setting in a Release build. Those are the two locks, and neither depends on
/// ASPNETCORE_ENVIRONMENT.
///
/// ACCEPTED EXPOSURE. The file holds the link and therefore the token plaintext,
/// as GmailTransport's Sent-folder copy does, and is bounded the same way — by
/// the token's lifetime. It is created owner-read/write from the moment it
/// exists, in a directory the sink never creates.
///
/// Like every transport it THROWS on refusal, and the sender records
/// NotSent/TransportFailed. Nothing about the message is logged except the file
/// it went to.
/// </summary>
internal sealed class DevelopmentMailSink : INotificationTransport
{
    /// <summary>Applied at creation, never afterwards, as ActivationTokenFile does.</summary>
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _directory;
    private readonly ILogger<DevelopmentMailSink> _logger;

    public DevelopmentMailSink(string directory, ILogger<DevelopmentMailSink> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(logger);

        _directory = directory;
        _logger = logger;
    }

    public async Task<string?> SendAsync(RenderedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // As GmailTransport refuses it, and for the same reason: a control
        // character in a header value would write a header line of its own.
        if (message.Recipient.Any(char.IsControl) || message.Subject.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "The message carries a control character in its recipient or subject, which would "
                + "inject a header line. Nothing was written.");
        }

        // Never created here: a missing directory is a configuration mistake,
        // and a link written somewhere unexpected is one nobody finds.
        if (!Directory.Exists(_directory))
        {
            throw new InvalidOperationException(
                "The development mail sink's directory does not exist, so nothing was written. "
                + "The sink never creates it.");
        }

        var name = $"{DateTime.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}.eml";

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OwnerOnly;

        await using (var stream = new FileStream(Path.Combine(_directory, name), options))
        {
            var content =
                $"To: {message.Recipient}\r\n"
                + $"Subject: {message.Subject}\r\n"
                + "Content-Type: text/plain; charset=utf-8\r\n"
                + "\r\n"
                + message.Body;

            await stream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
        }

        _logger.LogInformation("The development mail sink wrote a message to {File}.", name);

        return $"dev-sink:{name}";
    }
}
#endif
