namespace Ligature.Platform.Persistence.Notifications;

/// <summary>
/// Everything the mail transport needs, supplied by the composition root from
/// host configuration and never from the tenant database (§8.2, D-NOTIF-05).
///
/// The private key is the only secret here. The rest — sender address, display
/// name, service account identity — are configuration that may safely appear in
/// an error message, which is why they are separate fields rather than one
/// opaque blob: startup validation can say which setting is wrong without ever
/// quoting the key.
///
/// V1 has one sender for the whole host. Tenant-configurable sender
/// presentation is explicitly out of scope and would be a feature with its own
/// entity, not a configuration change.
/// </summary>
public sealed record MailSettings(
    string SenderAddress,
    string SenderName,
    string ServiceAccountEmail,
    string ServiceAccountPrivateKeyPem,
    TimeSpan TransportTimeout);
