namespace Ligature.Platform.Application.Notifications;

/// <summary>
/// The only public surface the host needs to run notification delivery, and
/// deliberately the narrowest one possible.
///
/// The host owns WHEN — start on boot, stop on shutdown. This owns WHAT: the
/// bounded channel, the consumers, the eligibility gate, rendering, transport
/// and the terminal write. The host never sees a declaration, a token plaintext
/// or a channel, because NotificationDeclaration is internal to this assembly
/// and the host has no InternalsVisibleTo. That is not an accident of layering;
/// it is what keeps the number of places a live activation secret can be
/// observed down to one assembly.
///
/// RunAsync returns when the token is cancelled, after a bounded drain.
/// </summary>
public interface INotificationPump
{
    Task RunAsync(CancellationToken stoppingToken);
}
