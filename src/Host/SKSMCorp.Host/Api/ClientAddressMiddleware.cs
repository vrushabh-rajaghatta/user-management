using System.Net;

namespace SKSMCorp.Host.Api;

/// <summary>
/// Resolves the client address behind trusted proxies (behaviour 11, B8;
/// docs/requirements.md, "Behaviour 11", The client address), and writes it
/// to <c>Connection.RemoteIpAddress</c> — the one place every consumer already
/// reads it: the rate limit, and the security evidence SES-C1 and CRD-C2
/// record. Their shape does not change; behind a configured proxy they simply
/// record the real caller instead of the proxy.
///
/// <para><b>X-Forwarded-For is authoritative only when the immediate peer is a
/// configured trusted proxy.</b> Otherwise it is ignored entirely.</para>
///
/// <para><b>The walk.</b> Starting from the peer, while the current hop is a
/// trusted proxy, the next address to the left becomes the candidate. The
/// first address that is not a trusted proxy is the client. An entry that is
/// not an address ends the walk, and the address stays the last one a trusted
/// hop established — never the malformed value.</para>
///
/// <para>Written here rather than with ASP.NET's ForwardedHeadersMiddleware,
/// which believes every header when its trusted lists are empty, and believes
/// the first entry unchecked when the connection has no address. Both are the
/// opposite of B8's default. Only X-Forwarded-For is read: X-Forwarded-Proto
/// and X-Forwarded-Host are ignored, because the cross-site check depends on
/// the request's own Host.</para>
/// </summary>
public sealed class ClientAddressMiddleware : IMiddleware
{
    private const string ForwardedFor = "X-Forwarded-For";

    private readonly IReadOnlyList<IPNetwork> _trusted;

    public ClientAddressMiddleware(IReadOnlyList<IPNetwork> trusted)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        _trusted = trusted;
    }

    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var resolved = Resolve(context.Connection.RemoteIpAddress, context.Request.Headers[ForwardedFor]);

        if (resolved is not null)
            context.Connection.RemoteIpAddress = resolved;

        return next(context);
    }

    private IPAddress? Resolve(IPAddress? peer, Microsoft.Extensions.Primitives.StringValues header)
    {
        if (_trusted.Count == 0 || peer is null || !IsTrusted(peer) || header.Count == 0)
            return null;

        var entries = header
            .SelectMany(value => (value ?? string.Empty).Split(','))
            .Select(entry => entry.Trim())
            .ToArray();

        var current = peer;

        for (var i = entries.Length - 1; i >= 0; i--)
        {
            if (!TryParseEntry(entries[i], out var candidate))
                break;

            current = candidate;

            if (!IsTrusted(current))
                break;
        }

        return current;
    }

    private bool IsTrusted(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return _trusted.Any(network => network.Contains(address));
    }

    /// <summary>An address, optionally with a port ("1.2.3.4:5", "[::1]:5").</summary>
    private static bool TryParseEntry(string entry, out IPAddress address)
    {
        address = IPAddress.None;

        if (entry.Length == 0 || !IPEndPoint.TryParse(entry, out var endpoint))
            return false;

        address = endpoint.Address;
        return true;
    }
}
