using SKSMCorp.Platform.Application.RateLimiting;

namespace SKSMCorp.Platform.Application.Tests.RateLimiting;

/// <summary>
/// How a raw value becomes a bucket key (docs/requirements.md, "Behaviour 11",
/// The keys). Null means no key: that gate does not apply to the request.
/// </summary>
public sealed class RateLimitKeysTests
{
    /// <summary>RL-7.</summary>
    [Theory]
    [InlineData("Ada")]
    [InlineData("ada")]
    [InlineData(" ada ")]
    [InlineData("\tADA\n")]
    public void An_address_is_trimmed_and_case_folded(string typed)
        => Assert.Equal("ada", RateLimitKeys.NormalizeAddress(typed));

    [Fact]
    public void An_email_address_folds_the_same_way()
        => Assert.Equal(
            "rl-case@example.test",
            RateLimitKeys.NormalizeAddress("  RL-Case@Example.TEST "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_address_has_no_key(string? typed)
        => Assert.Null(RateLimitKeys.NormalizeAddress(typed));

    [Fact]
    public void Different_ipv4_addresses_are_different_keys()
        => Assert.NotEqual(
            RateLimitKeys.NormalizeClientAddress("203.0.113.7"),
            RateLimitKeys.NormalizeClientAddress("203.0.113.8"));

    /// <summary>RL-18.</summary>
    [Fact]
    public void An_ipv4_mapped_ipv6_address_is_its_ipv4_address()
    {
        var key = RateLimitKeys.NormalizeClientAddress("::ffff:203.0.113.7");

        Assert.NotNull(key);
        Assert.Equal(RateLimitKeys.NormalizeClientAddress("203.0.113.7"), key);
    }

    /// <summary>RL-18.</summary>
    [Fact]
    public void Ipv6_addresses_in_one_slash_64_share_a_key()
    {
        var one = RateLimitKeys.NormalizeClientAddress("2001:db8:1:2:aaaa::1");
        var two = RateLimitKeys.NormalizeClientAddress("2001:db8:1:2:bbbb:cccc:dddd:2");

        Assert.NotNull(one);
        Assert.Equal(one, two);
    }

    /// <summary>RL-18.</summary>
    [Fact]
    public void Ipv6_addresses_in_different_slash_64s_do_not()
        => Assert.NotEqual(
            RateLimitKeys.NormalizeClientAddress("2001:db8:1:2::1"),
            RateLimitKeys.NormalizeClientAddress("2001:db8:1:3::1"));

    /// <summary>RL-19: no resolvable address, no IP bucket — never a shared "unknown" one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    public void No_resolvable_client_address_has_no_key(string? address)
        => Assert.Null(RateLimitKeys.NormalizeClientAddress(address));

    [Fact]
    public void Normalize_dispatches_on_the_kind()
    {
        Assert.Equal("ada", RateLimitKeys.Normalize(RateLimitKeyKind.Address, " Ada "));
        Assert.Null(RateLimitKeys.Normalize(RateLimitKeyKind.ClientAddress, "not-an-address"));
    }
}
