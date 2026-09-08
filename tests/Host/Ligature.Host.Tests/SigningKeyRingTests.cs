using Ligature.Host.Configuration;
using Microsoft.Extensions.Configuration;

namespace Ligature.Host.Tests;

/// <summary>
/// Startup validation, and it is a security control rather than tidiness.
///
/// Section 17: "there is no default, no hard-coded development key, and no
/// automatically generated key". Every test here is one way that rule could be
/// quietly reintroduced — a fallback, a short key, an unparsed value — and each
/// must stop the process rather than produce a ring that signs.
///
/// Pure: no database, no HTTP.
/// </summary>
public sealed class SigningKeyRingTests
{
    private const string ThirtyTwoBytes =
        "TGlnYXR1cmUgSG9zdCB0ZXN0IHNpZ25pbmcga2V5IDE=";

    [Fact]
    public void A_configured_ring_exposes_its_current_key()
    {
        var ring = Load(
            (SigningKeyRing.CurrentKeySetting, "v1"),
            ("LIGATURE_SIGNING_KEY_V1", ThirtyTwoBytes));

        Assert.Equal("v1", ring.CurrentKeyId);
        Assert.Equal(32, ring.CurrentKey.Length);

        // The identifier is lower-cased on the way in, so the environment
        // variable's shouting name and the carrier's quiet one agree.
        Assert.NotNull(ring.Find("v1"));
    }

    /// <summary>
    /// THE test this class exists for. A host that starts without a signing key
    /// would have to invent one, and a generated-per-restart key silently signs
    /// every user out on deploy while every test still passes.
    /// </summary>
    [Fact]
    public void A_missing_current_key_refuses_to_start()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Load(("LIGATURE_SIGNING_KEY_V1", ThirtyTwoBytes)));

        Assert.Contains(SigningKeyRing.CurrentKeySetting, failure.Message);
    }

    /// <summary>
    /// Naming a key that is not configured is the rotation mistake: retire
    /// LIGATURE_SIGNING_KEY_V1 but leave CURRENT pointing at v1, and a host
    /// that tolerated it would start and then fail on the first sign-in.
    /// </summary>
    [Fact]
    public void A_current_key_that_names_nothing_refuses_to_start()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Load(
                (SigningKeyRing.CurrentKeySetting, "v9"),
                ("LIGATURE_SIGNING_KEY_V1", ThirtyTwoBytes)));

        Assert.Contains("v9", failure.Message);
    }

    [Fact]
    public void A_key_shorter_than_the_minimum_refuses_to_start()
    {
        // Thirty-one bytes: one short, which is the only interesting length.
        var thirtyOne = Convert.ToBase64String(new byte[31]);

        var failure = Assert.Throws<InvalidOperationException>(
            () => Load(
                (SigningKeyRing.CurrentKeySetting, "v1"),
                ("LIGATURE_SIGNING_KEY_V1", thirtyOne)));

        Assert.Contains("31", failure.Message);
        Assert.Contains($"{SigningKeyRing.MinimumKeyBytes}", failure.Message);
    }

    [Fact]
    public void A_key_that_is_not_base64_refuses_to_start()
        => Assert.Throws<InvalidOperationException>(
            () => Load(
                (SigningKeyRing.CurrentKeySetting, "v1"),
                ("LIGATURE_SIGNING_KEY_V1", "not base64 at all !!")));

    [Fact]
    public void An_empty_key_value_refuses_to_start()
        => Assert.Throws<InvalidOperationException>(
            () => Load(
                (SigningKeyRing.CurrentKeySetting, "v1"),
                ("LIGATURE_SIGNING_KEY_V1", "")));

    /// <summary>
    /// A key identifier is the first component of a dot-separated carrier, so
    /// one containing a dot could not be parsed back out of it — the carrier
    /// would split into four parts and be rejected as malformed, which would
    /// look like a signing bug rather than a configuration one.
    /// </summary>
    [Fact]
    public void A_key_identifier_that_could_not_be_parsed_back_refuses_to_start()
        => Assert.Throws<InvalidOperationException>(
            () => Load(
                (SigningKeyRing.CurrentKeySetting, "v1"),
                ("LIGATURE_SIGNING_KEY_V1", ThirtyTwoBytes),
                ("LIGATURE_SIGNING_KEY_V1.OLD", ThirtyTwoBytes)));

    /// <summary>
    /// Rotation does not require a second key. The accepted set is whatever is
    /// configured, and v1 alone is the ordinary initial state rather than a
    /// half-finished rotation.
    /// </summary>
    [Fact]
    public void Additional_keys_verify_without_becoming_current()
    {
        var ring = Load(
            (SigningKeyRing.CurrentKeySetting, "v2"),
            ("LIGATURE_SIGNING_KEY_V1", ThirtyTwoBytes),
            ("LIGATURE_SIGNING_KEY_V2",
                Convert.ToBase64String(new byte[32])));

        Assert.Equal("v2", ring.CurrentKeyId);

        // The retired key still verifies outstanding carriers.
        Assert.NotNull(ring.Find("v1"));
        Assert.NotNull(ring.Find("v2"));

        Assert.Null(ring.Find("v3"));
    }

    private static SigningKeyRing Load(params (string Key, string Value)[] settings)
        => SigningKeyRing.Load(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    settings.Select(
                        x => new KeyValuePair<string, string?>(x.Key, x.Value)))
                .Build());
}
