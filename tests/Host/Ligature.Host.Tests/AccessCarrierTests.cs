using Ligature.Host.Authentication;
using Ligature.Host.Configuration;
using Ligature.Platform.Domain.Users;
using Microsoft.Extensions.Configuration;

namespace Ligature.Host.Tests;

/// <summary>
/// The signature is the entire security of the carrier: it is what stops a
/// caller writing their own SessionId into one. Everything here is a way that
/// could fail open.
///
/// Pure: no database, no HTTP.
/// </summary>
public sealed class AccessCarrierTests
{
    private const string PrimaryKey =
        "TGlnYXR1cmUgSG9zdCB0ZXN0IHNpZ25pbmcga2V5IDE=";

    private const string SecondaryKey =
        "TGlnYXR1cmUgSG9zdCB0ZXN0IHNpZ25pbmcga2V5IDI=";

    [Fact]
    public void A_carrier_round_trips_its_session_id()
    {
        var sessionId = UserSessionId.New();

        var verified = Carrier().Verify(Carrier().Issue(sessionId));

        Assert.Equal(sessionId, verified);
    }

    /// <summary>
    /// Section 17 fixes the shape, and it is worth asserting rather than
    /// assuming: three dot-separated components, the first being the key
    /// IDENTIFIER — not a format version.
    /// </summary>
    [Fact]
    public void A_carrier_has_three_components_and_names_its_key()
    {
        var components = Carrier().Issue(UserSessionId.New()).Split('.');

        Assert.Equal(3, components.Length);
        Assert.Equal("v1", components[0]);
    }

    /// <summary>
    /// The carrier must not be a second copy of anything the session row owns.
    /// A container with room for claims eventually gets some.
    /// </summary>
    [Fact]
    public void A_carrier_carries_nothing_but_the_key_id_and_the_session()
    {
        var sessionId = UserSessionId.New();

        var payload = Carrier().Issue(sessionId).Split('.')[1];

        // Sixteen bytes — a Guid and not one byte more. No issued-at, no
        // expiry, no user, no roles.
        Assert.Equal(
            16, System.Buffers.Text.Base64Url.DecodeFromChars(payload).Length);
    }

    /// <summary>
    /// THE test. Flip one character of the signature and the carrier must die.
    /// </summary>
    [Fact]
    public void A_tampered_signature_is_rejected()
    {
        var components = Carrier().Issue(UserSessionId.New()).Split('.');

        var signature = components[2].ToCharArray();
        signature[0] = signature[0] == 'A' ? 'B' : 'A';

        Assert.Null(
            Carrier().Verify(
                $"{components[0]}.{components[1]}.{new string(signature)}"));
    }

    /// <summary>
    /// The forgery that matters: swap in a session id you want to be, keep a
    /// signature that was valid for a different one. Without the signature
    /// covering the payload this would succeed and every session would be
    /// impersonable.
    /// </summary>
    [Fact]
    public void A_substituted_session_id_is_rejected()
    {
        var components = Carrier().Issue(UserSessionId.New()).Split('.');

        var attacker = System.Buffers.Text.Base64Url.EncodeToString(
            UserSessionId.New().Value.ToByteArray());

        Assert.Null(
            Carrier().Verify($"{components[0]}.{attacker}.{components[2]}"));
    }

    /// <summary>
    /// The key identifier is inside the signed portion, so relabelling a
    /// carrier as belonging to another configured key must also fail — even
    /// though that key is legitimately configured and would verify carriers of
    /// its own.
    /// </summary>
    [Fact]
    public void A_carrier_relabelled_with_another_configured_key_is_rejected()
    {
        var components = Carrier().Issue(UserSessionId.New()).Split('.');

        Assert.Null(
            Carrier().Verify($"v2.{components[1]}.{components[2]}"));
    }

    [Fact]
    public void An_unknown_key_identifier_is_rejected()
    {
        var components = Carrier().Issue(UserSessionId.New()).Split('.');

        Assert.Null(
            Carrier().Verify($"v9.{components[1]}.{components[2]}"));
    }

    /// <summary>
    /// A carrier signed by a key that is no longer configured stops verifying.
    /// That is the intended cost of retiring a key, and it destroys no session
    /// state — the rows are all still there.
    /// </summary>
    [Fact]
    public void A_carrier_signed_by_a_retired_key_stops_verifying()
    {
        var issued = Carrier().Issue(UserSessionId.New());

        var afterRotation = Carrier(
            currentKeyId: "v2",
            ("LIGATURE_SIGNING_KEY_V2", SecondaryKey));

        Assert.Null(afterRotation.Verify(issued));
    }

    /// <summary>
    /// A carrier signed by a key that is still ACCEPTED keeps working while it
    /// is no longer the one signing. That is the other half of rotation, and
    /// without it every rotation would sign every user out.
    /// </summary>
    [Fact]
    public void A_carrier_signed_by_a_still_accepted_key_survives_rotation()
    {
        var sessionId = UserSessionId.New();

        var issued = Carrier().Issue(sessionId);

        var afterRotation = Carrier(
            currentKeyId: "v2",
            ("LIGATURE_SIGNING_KEY_V1", PrimaryKey),
            ("LIGATURE_SIGNING_KEY_V2", SecondaryKey));

        Assert.Equal(sessionId, afterRotation.Verify(issued));

        // And new carriers are signed by the new key.
        Assert.Equal("v2", afterRotation.Issue(sessionId).Split('.')[0]);
    }

    /// <summary>
    /// Every malformed shape returns null rather than throwing. These are
    /// caller-supplied strings arriving on an unauthenticated path: an
    /// exception here is a 500 that anyone can trigger at will.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("v1.only-two")]
    [InlineData("v1.a.b.c")]
    [InlineData("v1..")]
    [InlineData("v1.!!!.???")]
    public void A_malformed_carrier_is_rejected_without_throwing(string? carrier)
        => Assert.Null(Carrier().Verify(carrier));

    /// <summary>
    /// An all-zero payload decodes to Guid.Empty, which StronglyTypedId
    /// refuses. Correctly signed, so it reaches the constructor: without the
    /// catch this is a DomainException escaping from middleware, and anyone
    /// holding the key could... but more to the point, it must be one more
    /// indistinguishable rejection.
    /// </summary>
    [Fact]
    public void A_correctly_signed_empty_guid_is_rejected_without_throwing()
    {
        var payload =
            System.Buffers.Text.Base64Url.EncodeToString(Guid.Empty.ToByteArray());

        var unsigned = $"v1.{payload}";

        var signature = System.Buffers.Text.Base64Url.EncodeToString(
            System.Security.Cryptography.HMACSHA256.HashData(
                Convert.FromBase64String(PrimaryKey),
                System.Text.Encoding.UTF8.GetBytes(unsigned)));

        Assert.Null(Carrier().Verify($"{unsigned}.{signature}"));
    }

    /// <summary>
    /// A correctly signed payload of the wrong LENGTH must not reach the Guid
    /// constructor, which throws on anything but sixteen bytes.
    /// </summary>
    [Fact]
    public void A_correctly_signed_payload_of_the_wrong_length_is_rejected()
    {
        var payload = System.Buffers.Text.Base64Url.EncodeToString(new byte[8]);

        var unsigned = $"v1.{payload}";

        var signature = System.Buffers.Text.Base64Url.EncodeToString(
            System.Security.Cryptography.HMACSHA256.HashData(
                Convert.FromBase64String(PrimaryKey),
                System.Text.Encoding.UTF8.GetBytes(unsigned)));

        Assert.Null(Carrier().Verify($"{unsigned}.{signature}"));
    }

    private static AccessCarrier Carrier(
        string currentKeyId = "v1",
        params (string Key, string Value)[] keys)
    {
        if (keys.Length == 0)
            keys = [("LIGATURE_SIGNING_KEY_V1", PrimaryKey),
                    ("LIGATURE_SIGNING_KEY_V2", SecondaryKey)];

        var settings = keys
            .Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))
            .Append(new KeyValuePair<string, string?>(
                SigningKeyRing.CurrentKeySetting, currentKeyId));

        return new AccessCarrier(
            SigningKeyRing.Load(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(settings)
                    .Build()));
    }
}
