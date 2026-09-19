using System.Net;
using Ligature.Host.Configuration;
using Microsoft.Extensions.Configuration;
using static Ligature.Host.Tests.RateLimitHarness;

namespace Ligature.Host.Tests;

/// <summary>
/// The client address behind a proxy (docs/requirements.md, "Behaviour 11",
/// The client address; B8). X-Forwarded-For is authoritative ONLY when the
/// immediate peer is a configured trusted proxy — never merely because the
/// header is present, and loopback is not trusted implicitly.
///
/// The recorded evidence is what shows the resolved address: SignInFailed's
/// IpAddress for an attempted identifier nobody else uses.
/// </summary>
public sealed class TrustedProxyTests
{
    private static HostFactory Trusting(string proxies)
        => new(settings: new Dictionary<string, string>
        {
            [HostConfiguration.TrustedProxiesSetting] = proxies,
        });

    private static async Task<string?> RecordedAddressAsync(
        HostFactory factory, string peer, string? forwardedFor)
    {
        var username = Fresh("rl-proxy");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await SignInAsync(factory, username, peer: peer, forwardedFor: forwardedFor)).Status);

        return await SignInFailedAddressAsync(username);
    }

    // ---------------------------------------------------- untrusted (RL-22)

    [Fact]
    public async Task With_nothing_configured_even_loopback_is_not_believed()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory();

        Assert.Equal("127.0.0.1", await RecordedAddressAsync(factory, "127.0.0.1", "203.0.113.50"));
    }

    [Fact]
    public async Task An_untrusted_header_does_not_move_the_ip_bucket()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory();

        // Thirty different claimed addresses, one real peer: one bucket.
        for (var i = 0; i < 30; i++)
        {
            await SignInAsync(
                factory, Fresh("rl-proxy"), peer: "192.0.2.10", forwardedFor: $"203.0.113.{i + 100}");
        }

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await SignInAsync(factory, Fresh("rl-proxy"), peer: "192.0.2.10", forwardedFor: "203.0.113.200")).Status);
    }

    [Fact]
    public async Task A_peer_that_is_not_a_configured_proxy_is_not_believed()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting("10.0.0.5");

        Assert.Equal("192.0.2.1", await RecordedAddressAsync(factory, "192.0.2.1", "203.0.113.91"));
    }

    // ------------------------------------------------------ trusted (RL-23)

    [Fact]
    public async Task A_trusted_proxys_header_is_the_recorded_address()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting("10.0.0.5");

        Assert.Equal("203.0.113.60", await RecordedAddressAsync(factory, "10.0.0.5", "203.0.113.60"));
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_the_session_and_sign_in_record_the_real_caller()
    {
        await EnsureSignerAsync();
        await using var factory = Trusting("10.0.0.5");

        var signedIn = await SignInAsync(
            factory, SignerUsername, Password, peer: "10.0.0.5", forwardedFor: "203.0.113.61");

        Assert.Equal(HttpStatusCode.NoContent, signedIn.Status);

        Assert.Equal(
            "203.0.113.61",
            await ScalarAsync<string?>(
                "SELECT host(ip_address) FROM user_session WHERE user_identity_id = @id",
                ("id", SignerIdentity)));

        Assert.Equal(
            "203.0.113.61",
            await ScalarAsync<string?>(
                """
                SELECT r.payload ->> 'IpAddress' FROM audit.audit_record r
                JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
                WHERE r.event_type = 'SignInSucceeded' AND e.entity_id = @id
                ORDER BY r.sequence DESC LIMIT 1
                """,
                ("id", SignerIdentity)));
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_a_reset_request_records_the_real_caller()
    {
        await EnsureSignerAsync();
        await using var factory = Trusting("10.0.0.5");

        Assert.Equal(
            HttpStatusCode.OK,
            (await RequestResetAsync(factory, SignerEmail, peer: "10.0.0.5", forwardedFor: "203.0.113.62")).Status);

        Assert.Equal(
            "203.0.113.62",
            await ScalarAsync<string?>(
                """
                SELECT r.payload ->> 'RequestIp' FROM audit.audit_record r
                JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
                WHERE r.event_type = 'PasswordResetRequested' AND e.entity_id = @id
                ORDER BY r.sequence DESC LIMIT 1
                """,
                ("id", SignerIdentity)));
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_each_real_caller_has_its_own_bucket()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting("10.0.0.5");

        // Thirty-one callers through one proxy: none is refused.
        for (var i = 0; i < 31; i++)
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await SignInAsync(factory, Fresh("rl-proxy"), peer: "10.0.0.5", forwardedFor: $"203.0.113.{i + 100}")).Status);
        }

        // One caller, thirty-one times: the 31st is.
        for (var i = 0; i < 30; i++)
            await SignInAsync(factory, Fresh("rl-proxy"), peer: "10.0.0.5", forwardedFor: "198.51.100.63");

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await SignInAsync(factory, Fresh("rl-proxy"), peer: "10.0.0.5", forwardedFor: "198.51.100.63")).Status);
    }

    // --------------------------------------------------------- the walk (RL-24)

    [Fact]
    public async Task A_chain_of_trusted_proxies_resolves_the_first_untrusted_address()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting("10.0.0.5,10.0.0.6");

        Assert.Equal(
            "203.0.113.70",
            await RecordedAddressAsync(factory, "10.0.0.6", "198.51.100.1, 203.0.113.70, 10.0.0.5"));
    }

    [Fact]
    public async Task A_spoofed_entry_behind_one_trusted_proxy_is_not_reached()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting("10.0.0.5");

        Assert.Equal(
            "203.0.113.80",
            await RecordedAddressAsync(factory, "10.0.0.5", "198.51.100.99, 203.0.113.80"));
    }

    [Fact]
    public async Task A_malformed_entry_is_never_the_address()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting("10.0.0.5");

        Assert.Equal("10.0.0.5", await RecordedAddressAsync(factory, "10.0.0.5", "203.0.113.90, garbage"));
    }

    // --------------------------------------------------- configuration (RL-25)

    [Fact]
    public async Task A_cidr_range_is_accepted()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting("10.0.0.0/24");

        Assert.Equal("203.0.113.95", await RecordedAddressAsync(factory, "10.0.0.77", "203.0.113.95"));
    }

    [Fact]
    public async Task An_empty_setting_trusts_nothing()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting("  ");

        Assert.Equal("127.0.0.1", await RecordedAddressAsync(factory, "127.0.0.1", "203.0.113.96"));
    }

    [Theory]
    [InlineData("10.0.0.300")]
    [InlineData("10.0.0.0/33")]
    [InlineData("banana")]
    [InlineData("10.0.0.5,,10.0.0.6")]
    public async Task A_malformed_entry_stops_the_host(string proxies)
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting(proxies);

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    /// <summary>
    /// ASP.NET's own forwarded-headers switch believes X-Forwarded-For from any
    /// peer, which would undo B8 beside this host's list. It stops the host.
    /// </summary>
    [Fact]
    public async Task The_frameworks_trust_everyone_switch_stops_the_host()
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = new HostFactory(settings: new Dictionary<string, string>
        {
            [TrustedProxies.FrameworkForwardedHeadersSetting] = "true",
        });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("10.5")]
    [InlineData("1")]
    public async Task A_shorthand_address_is_refused_rather_than_trusted(string proxies)
    {
        await TestDatabase.EnsureProvisionedAsync();
        await using var factory = Trusting(proxies);

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void The_loader_parses_addresses_and_ranges()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [HostConfiguration.TrustedProxiesSetting] = " 10.0.0.5 , 192.168.0.0/16,2001:db8::1 ",
            })
            .Build();

        var networks = TrustedProxies.Load(configuration);

        Assert.Equal(3, networks.Count);
        Assert.Contains(networks, x => x.Contains(IPAddress.Parse("10.0.0.5")));
        Assert.Contains(networks, x => x.Contains(IPAddress.Parse("192.168.44.1")));
        Assert.Contains(networks, x => x.Contains(IPAddress.Parse("2001:db8::1")));
        Assert.DoesNotContain(networks, x => x.Contains(IPAddress.Parse("10.0.0.6")));
    }
}
