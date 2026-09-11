using Ligature.Platform.Application.Notifications;

namespace Ligature.Host.Notifications;

/// <summary>
/// Runs notification delivery for the life of the host, and does nothing else.
///
/// Deliberately this thin. Hosting is a host concern, so BackgroundService
/// lives here; what to send, when to give up, how to shed and when to sweep are
/// application policy, so they live behind INotificationPump. That split is not
/// only tidiness: NotificationDeclaration is internal to the Application
/// assembly and the host has no InternalsVisibleTo, so this type CANNOT see a
/// declaration or a token plaintext even by accident. The number of places a
/// live activation secret can be observed stays at one assembly.
///
/// Registered whether or not mail is configured, because the pump sweeps in
/// both modes. A host with no mail still has to give its Pending rows a
/// terminus.
///
/// ExecuteAsync returning is normal here rather than a failure: the pump
/// returns when stopping has been requested and the bounded drain has elapsed.
/// </summary>
public sealed class NotificationSenderService : BackgroundService
{
    private readonly INotificationPump _pump;

    public NotificationSenderService(INotificationPump pump)
        => _pump = pump;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => _pump.RunAsync(stoppingToken);
}
