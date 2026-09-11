using System.Text;
using Ligature.Host.Configuration;
using Microsoft.Extensions.Configuration;

namespace Ligature.Host.Tests;

/// <summary>
/// The absent / partial / complete rule.
///
/// The middle case is the one worth refusing. A half-configured mail setup is
/// someone believing mail works when it does not, and finding out from a user
/// who never received an activation link. Total absence is legible: nothing was
/// set up, nothing is claimed, and every notification records honestly that no
/// attempt was observed.
/// </summary>
public sealed class MailConfigurationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void No_mail_settings_at_all_is_a_valid_state()
    {
        // up.sh can generate a signing key; it cannot generate a Google service
        // account. Refusing to start here would break the promise that a clean
        // clone reaches a running host in one command.
        Assert.Null(MailConfiguration.Load(Build([]), Timeout));
    }

    [Fact]
    public void A_partially_configured_host_refuses_to_start()
    {
        var partial = Complete();
        partial.Remove(HostConfiguration.MailServiceAccountKeySetting);

        var failure = Assert.Throws<InvalidOperationException>(
            () => MailConfiguration.Load(Build(partial), Timeout));

        Assert.Contains(
            HostConfiguration.MailServiceAccountKeySetting,
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_complete_configuration_loads()
    {
        var loaded = MailConfiguration.Load(Build(Complete()), Timeout);

        Assert.NotNull(loaded);
        Assert.Equal("rdtech@example.com", loaded!.Value.Settings.SenderAddress);
        Assert.Equal(Timeout, loaded.Value.Settings.TransportTimeout);
        Assert.Equal("https://app.example.com/", loaded.Value.PublicBaseUrl.AbsoluteUri);
        Assert.Contains(
            "PRIVATE KEY",
            loaded.Value.Settings.ServiceAccountPrivateKeyPem,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_http_base_url_is_refused()
    {
        var settings = Complete();
        settings[HostConfiguration.PublicBaseUrlSetting] = "http://app.example.com";

        var failure = Assert.Throws<InvalidOperationException>(
            () => MailConfiguration.Load(Build(settings), Timeout));

        // The link carries a token that can set a password. Over http it would
        // travel in clear text to anyone on the path.
        Assert.Contains("https", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_base_url_carrying_a_fragment_is_refused()
    {
        var settings = Complete();
        settings[HostConfiguration.PublicBaseUrlSetting] = "https://app.example.com/#x";

        // The fragment is where the token goes; a configured one would be
        // silently overwritten.
        Assert.Throws<InvalidOperationException>(
            () => MailConfiguration.Load(Build(settings), Timeout));
    }

    [Fact]
    public void A_key_that_is_not_base64_is_refused_by_name()
    {
        var settings = Complete();
        settings[HostConfiguration.MailServiceAccountKeySetting] = "not base64 !!";

        var failure = Assert.Throws<InvalidOperationException>(
            () => MailConfiguration.Load(Build(settings), Timeout));

        // Names the setting, never the value — an error that echoed key
        // material would put it in every log that captured the failure.
        Assert.Contains(
            HostConfiguration.MailServiceAccountKeySetting,
            failure.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain("not base64", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Base64_that_is_not_a_pem_key_is_refused()
    {
        var settings = Complete();

        settings[HostConfiguration.MailServiceAccountKeySetting] =
            Convert.ToBase64String(Encoding.UTF8.GetBytes("just some text"));

        Assert.Throws<InvalidOperationException>(
            () => MailConfiguration.Load(Build(settings), Timeout));
    }

    private static Dictionary<string, string?> Complete()
        => new()
        {
            [HostConfiguration.PublicBaseUrlSetting] = "https://app.example.com",
            [HostConfiguration.MailSenderAddressSetting] = "rdtech@example.com",
            [HostConfiguration.MailSenderNameSetting] = "Ligature",
            [HostConfiguration.MailServiceAccountSetting] = "svc@project.iam.gserviceaccount.com",
            [HostConfiguration.MailServiceAccountKeySetting] =
                Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----\n")),
        };

    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
