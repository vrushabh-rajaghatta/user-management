using System.Net;

namespace Ligature.Host.Configuration;

/// <summary>
/// The trusted-proxy list (behaviour 11, B8; docs/requirements.md, "The client
/// address").
/// </summary>
public static class TrustedProxies
{
    // RED-TEST STUB: trusts nothing and validates nothing.
    public static IReadOnlyList<IPNetwork> Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Array.Empty<IPNetwork>();
    }
}
