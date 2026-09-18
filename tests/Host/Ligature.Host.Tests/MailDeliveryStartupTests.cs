using System.Text;
using Ligature.Host.Configuration;

namespace Ligature.Host.Tests;

/// <summary>
/// A host WITH mail configured must start. Every other host test runs with mail
/// unconfigured, which registers no sender at all — so until this suite, nothing
/// had ever built the delivery services the configured path registers, and a
/// defect there would surface only on a deployment with real credentials.
///
/// Nothing is sent: starting the host builds the sender and its transport, and
/// neither talks to Google until a notification is handed off.
/// </summary>
public sealed class MailDeliveryStartupTests
{
    [Fact]
    public async Task A_host_with_complete_gmail_configuration_starts()
    {
        await using var factory = new HostFactory(settings: new Dictionary<string, string>
        {
            [HostConfiguration.PublicBaseUrlSetting] = "https://app.example.com",
            [HostConfiguration.MailSenderAddressSetting] = "rdtech@example.com",
            [HostConfiguration.MailSenderNameSetting] = "Ligature",
            [HostConfiguration.MailServiceAccountSetting] = "svc@project.iam.gserviceaccount.com",
            [HostConfiguration.MailServiceAccountKeySetting] = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----\n")),
        });

        // Creating the client starts the host, and with it the hosted sender.
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
