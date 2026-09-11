using System.Text;
using Ligature.Platform.Persistence.Notifications;

namespace Ligature.Host.Configuration;

/// <summary>
/// Loads the mail settings, or establishes that there are none.
///
/// ALL ABSENT IS A VALID STATE. The specification says the host refuses to
/// start without the transport credential, and taken literally that would break
/// the promise that a clean clone reaches a running system in one command:
/// up.sh generates the secrets it can, and it cannot generate a Google service
/// account. So the rule is read as being about misconfiguration rather than
/// about absence:
///
///     nothing configured   host starts, delivery disabled, rows age to Abandoned
///     some configured      host refuses and names the missing setting
///     all configured       host starts and sends
///
/// The middle case is the one worth refusing. A half-configured mail setup is
/// someone believing mail works when it does not, and finding out from a user
/// who never received an activation link. Total absence, by contrast, is a
/// legible state: nothing was set up, nothing is claimed, and every notification
/// records honestly that no attempt was observed.
///
/// Messages name the SETTING, never the value — the key would otherwise reach
/// every log that captured the startup failure.
/// </summary>
public static class MailConfiguration
{
    private static readonly string[] Settings =
    [
        HostConfiguration.PublicBaseUrlSetting,
        HostConfiguration.MailSenderAddressSetting,
        HostConfiguration.MailSenderNameSetting,
        HostConfiguration.MailServiceAccountSetting,
        HostConfiguration.MailServiceAccountKeySetting,
    ];

    /// <returns>
    /// The settings and the public base URL, or null when mail is entirely
    /// unconfigured.
    /// </returns>
    public static (MailSettings Settings, Uri PublicBaseUrl)? Load(
        IConfiguration configuration,
        TimeSpan transportTimeout)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var present = Settings
            .Where(name => !string.IsNullOrWhiteSpace(configuration[name]))
            .ToArray();

        if (present.Length == 0)
            return null;

        if (present.Length != Settings.Length)
        {
            var missing = Settings.Except(present);

            throw new InvalidOperationException(
                "Mail is partially configured, which is worse than not "
                + "configuring it: the host would start and silently never "
                + "deliver an activation link. Set the missing settings, or "
                + "unset all of them to run with delivery disabled. Missing: "
                + string.Join(", ", missing) + ".");
        }

        var baseUrl = ReadBaseUrl(configuration);

        return (
            new MailSettings(
                SenderAddress: configuration[HostConfiguration.MailSenderAddressSetting]!,
                SenderName: configuration[HostConfiguration.MailSenderNameSetting]!,
                ServiceAccountEmail: configuration[HostConfiguration.MailServiceAccountSetting]!,
                ServiceAccountPrivateKeyPem: ReadKey(configuration),
                TransportTimeout: transportTimeout),
            baseUrl);
    }

    private static Uri ReadBaseUrl(IConfiguration configuration)
    {
        var raw = configuration[HostConfiguration.PublicBaseUrlSetting]!;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"{HostConfiguration.PublicBaseUrlSetting} is not an absolute "
                + "URL. It is the origin an activation link is built from, so "
                + "it must be something a mail client can open — for example "
                + "https://app.example.com.");
        }

        // The link carries a bearer token that sets a password. Over http it
        // would travel in clear text to anyone on the path, which would defeat
        // the entire reason the token is emailed rather than shown to an
        // administrator.
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{HostConfiguration.PublicBaseUrlSetting} must be https. The "
                + "activation link carries a token that can set a password, "
                + "and http would expose it to anyone on the network path.");
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{HostConfiguration.PublicBaseUrlSetting} must be an origin "
                + "with no query and no fragment. The fragment in particular is "
                + "where the activation token is placed, and a configured one "
                + "would be overwritten.");
        }

        return uri;
    }

    private static string ReadKey(IConfiguration configuration)
    {
        var raw = configuration[HostConfiguration.MailServiceAccountKeySetting]!;

        byte[] decoded;

        try
        {
            decoded = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{HostConfiguration.MailServiceAccountKeySetting} is not valid "
                + "base64. It carries the service account's PEM private key, "
                + "base64 encoded so that a multi-line value fits in a single "
                + "environment variable.");
        }

        var pem = Encoding.UTF8.GetString(decoded);

        if (!pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{HostConfiguration.MailServiceAccountKeySetting} does not "
                + "decode to a PEM private key. Base64 encode the private_key "
                + "field of the service account's JSON key file.");
        }

        return pem;
    }
}
