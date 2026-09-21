using System.Net;
using System.Net.Sockets;

namespace SKSMCorp.Platform.Application.RateLimiting;

/// <summary>
/// How a raw value becomes a bucket key (docs/requirements.md, "Behaviour 11",
/// The keys). Null means "no key": the gate for that kind does not apply to the
/// request. Never a shared "unknown" key — that would collapse every caller
/// without an address into one global bucket.
/// </summary>
public static class RateLimitKeys
{
    public static string? Normalize(RateLimitKeyKind kind, string? value)
        => kind switch
        {
            RateLimitKeyKind.Address => NormalizeAddress(value),
            RateLimitKeyKind.ClientAddress => NormalizeClientAddress(value),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    /// <summary>
    /// Trimmed and folded with the invariant culture, so the key is at least as
    /// coarse as the database's lower(…) match for ordinary input: "Ada",
    /// "ada" and " ada " share a bucket.
    ///
    /// ACCEPTED LIMITATION: this is .NET's invariant fold, not PostgreSQL's
    /// lower() under the database collation. For the few characters where they
    /// differ, two spellings the database treats as one could draw on separate
    /// buckets. Matching exactly would cost a database round-trip before
    /// admission, which is what running first exists to avoid.
    /// </summary>
    public static string? NormalizeAddress(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// An IPv4-mapped IPv6 address is its IPv4 address. Any other IPv6 address
    /// is keyed by its /64, because one subscriber is routinely given a whole
    /// /64 and could otherwise rotate through it. Anything that is not an
    /// address has no key.
    /// </summary>
    public static string? NormalizeClientAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IPAddress.TryParse(value.Trim(), out var address))
            return null;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);

        return $"{new IPAddress(bytes)}/64";
    }
}
