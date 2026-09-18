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
        var loaded = Assert.IsType<GmailDelivery>(MailConfiguration.Load(Build(Complete()), Timeout));

        Assert.NotNull(loaded);
        Assert.Equal("rdtech@example.com", loaded.Settings.SenderAddress);
        Assert.Equal(Timeout, loaded.Settings.TransportTimeout);
        Assert.Equal("https://app.example.com/", loaded.PublicBaseUrl.AbsoluteUri);
        Assert.Contains(
            "PRIVATE KEY",
            loaded.Settings.ServiceAccountPrivateKeyPem,
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

    // ------------------------------------------------ development mail sink

    /// <summary>
    /// docs/architecture.md §8, "development mail sink". The sink and the base
    /// URL its links are built from, and nothing of Gmail's.
    /// </summary>
    [Fact]
    public void The_sink_with_a_base_url_loads_as_the_development_sink()
    {
        using var directory = new TemporaryDirectory();

        var loaded = Assert.IsType<DevelopmentSinkDelivery>(
            MailConfiguration.Load(Build(Sink(directory.Path)), Timeout));

        Assert.Equal(directory.Path, loaded.Directory);
        Assert.Equal("https://localhost:5173/", loaded.PublicBaseUrl.AbsoluteUri);
    }

    [Fact]
    public void The_sink_without_a_base_url_is_refused_by_name()
    {
        using var directory = new TemporaryDirectory();

        var settings = Sink(directory.Path);
        settings.Remove(HostConfiguration.PublicBaseUrlSetting);

        var failure = Assert.Throws<InvalidOperationException>(
            () => MailConfiguration.Load(Build(settings), Timeout));

        Assert.Contains(HostConfiguration.PublicBaseUrlSetting, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Two transports at once is a mistake, whichever Gmail setting it is.</summary>
    [Theory]
    [InlineData(HostConfiguration.MailSenderAddressSetting)]
    [InlineData(HostConfiguration.MailSenderNameSetting)]
    [InlineData(HostConfiguration.MailServiceAccountSetting)]
    [InlineData(HostConfiguration.MailServiceAccountKeySetting)]
    public void The_sink_beside_any_gmail_setting_is_refused_by_name(string gmailSetting)
    {
        using var directory = new TemporaryDirectory();

        var settings = Sink(directory.Path);
        settings[gmailSetting] = Complete()[gmailSetting];

        var failure = Assert.Throws<InvalidOperationException>(
            () => MailConfiguration.Load(Build(settings), Timeout));

        Assert.Contains(HostConfiguration.MailDevSinkDirectorySetting, failure.Message, StringComparison.Ordinal);
        Assert.Contains(gmailSetting, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sink never creates its directory, and a missing one is caught here,
    /// at start-up, rather than as a TransportFailed row on the first send.
    /// </summary>
    [Fact]
    public void The_sink_naming_a_directory_that_does_not_exist_is_refused_by_name()
    {
        using var directory = new TemporaryDirectory();

        var failure = Assert.Throws<InvalidOperationException>(
            () => MailConfiguration.Load(Build(Sink(System.IO.Path.Combine(directory.Path, "missing"))), Timeout));

        Assert.Contains(HostConfiguration.MailDevSinkDirectorySetting, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sink_is_held_to_the_same_base_url_rules_as_gmail()
    {
        using var directory = new TemporaryDirectory();

        var settings = Sink(directory.Path);
        settings[HostConfiguration.PublicBaseUrlSetting] = "http://localhost:5173";

        Assert.Throws<InvalidOperationException>(() => MailConfiguration.Load(Build(settings), Timeout));
    }

    private static Dictionary<string, string?> Sink(string directory)
        => new()
        {
            [HostConfiguration.PublicBaseUrlSetting] = "https://localhost:5173",
            [HostConfiguration.MailDevSinkDirectorySetting] = directory,
        };

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("ligature-sink-config-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
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
