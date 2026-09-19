using System.Net;

namespace Ligature.Host.Configuration;

/// <summary>
/// The proxies whose X-Forwarded-For is believed (behaviour 11, B8;
/// docs/requirements.md, "Behaviour 11", The client address).
///
/// <para><b>Absent or empty trusts nothing</b> — loopback included. The safe
/// state is the one you get by doing nothing.</para>
///
/// <para><b>A malformed entry stops the host</b>, as a malformed signing key or
/// documentation flag does. A typo in a proxy list would otherwise be
/// indistinguishable from a deliberate refusal, and the symptom — every caller
/// behind the proxy sharing one bucket — would surface only under load.</para>
/// </summary>
public static class TrustedProxies
{
    /// <summary>
    /// ASP.NET's own switch for its forwarded-headers startup filter, read from
    /// ASPNETCORE_FORWARDEDHEADERS_ENABLED. When on, that filter believes
    /// X-Forwarded-For from ANY peer — exactly what B8 forbids — so the host
    /// refuses to start with it rather than run with two disagreeing rules.
    /// </summary>
    public const string FrameworkForwardedHeadersSetting = "FORWARDEDHEADERS_ENABLED";

    public static IReadOnlyList<IPNetwork> Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (bool.TryParse(configuration[FrameworkForwardedHeadersSetting], out var frameworkEnabled)
            && frameworkEnabled)
        {
            throw new InvalidOperationException(
                "ASPNETCORE_FORWARDEDHEADERS_ENABLED is set. It makes ASP.NET believe "
                + "X-Forwarded-For from any peer. Unset it and list the trusted proxies in "
                + $"{HostConfiguration.TrustedProxiesSetting} instead.");
        }

        var value = configuration[HostConfiguration.TrustedProxiesSetting];

        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',').Select(entry => Parse(entry.Trim())).ToList();
    }

    private static IPNetwork Parse(string entry)
    {
        if (entry.Contains('/', StringComparison.Ordinal))
        {
            if (IPNetwork.TryParse(entry, out var network))
                return network;
        }
        else if (IsAddress(entry, out var address))
        {
            return new IPNetwork(
                address,
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
        }

        throw new InvalidOperationException(
            $"{HostConfiguration.TrustedProxiesSetting} contains \"{entry}\", which is not "
            + "an IP address or a CIDR range. It is a comma-separated list, for "
            + "example \"10.0.0.5,192.168.0.0/16\".");
    }

    /// <summary>
    /// Strict: IPAddress.TryParse also accepts shorthand such as "10.5" or
    /// "1", which would trust an address nobody meant. An IPv4 entry must have
    /// its four parts.
    /// </summary>
    private static bool IsAddress(string entry, out IPAddress address)
    {
        address = IPAddress.None;

        if (entry.Length == 0 || !IPAddress.TryParse(entry, out var parsed))
            return false;

        if (!entry.Contains(':', StringComparison.Ordinal) && entry.Split('.').Length != 4)
            return false;

        address = parsed;
        return true;
    }
}
