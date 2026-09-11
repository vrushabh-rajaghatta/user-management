using System.Net;
using System.Text;
using System.Text.Json;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Persistence.Notifications;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// The message the transport actually puts on the wire, and what it does when
/// Google rejects the credential.
///
/// Everything here runs against a stubbed handler, so it proves composition and
/// failure handling rather than connectivity. It cannot prove Google accepts the
/// message — only a real send can — which is why the composition rules are
/// asserted against the RFCs rather than against a provider's tolerance.
/// </summary>
public sealed class GmailTransportTests
{
    private const string Secret = "abc.DEF-ghi_jkl";

    [Fact]
    public async Task A_recipient_carrying_a_newline_is_refused_before_composing()
    {
        var handler = new CapturingHandler();

        // EmailAddress does not reject control characters, and the CRLF
        // normalisation in Compose would turn this into a WELL-FORMED injected
        // header rather than corrupting it into harmlessness.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(handler, recipient: "victim@example.com\r\nSubject: Hijacked"));

        // Fail closed: nothing was composed and nothing was sent.
        Assert.Null(handler.LastBody);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\t")]
    [InlineData("\0")]
    public async Task Any_control_character_in_the_recipient_is_refused(string injected)
    {
        var handler = new CapturingHandler();

        // Deliberately broader than CR and LF: the guard should not depend on
        // which characters today's formatter happens to treat as significant.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(handler, recipient: $"victim@example.com{injected}x"));

        Assert.Null(handler.LastBody);
    }

    [Fact]
    public async Task The_refusal_does_not_quote_the_offending_value()
    {
        var handler = new CapturingHandler();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(handler, recipient: "victim@example.com\r\nSubject: Hijacked"));

        Assert.DoesNotContain("Hijacked", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_body_is_folded_within_the_mime_line_limit()
    {
        var handler = new CapturingHandler();

        // Comfortably longer than one unwrapped base64 line would be.
        await SendAsync(handler, body: new string('x', 4000));

        foreach (var line in Decode(handler.LastBody!).Split("\r\n"))
        {
            // RFC 2045 §6.8 wants 76; RFC 5322 §2.1.1 caps any line at 998, and
            // a fold inserted by a downstream agent inside an unwrapped base64
            // run would corrupt it silently.
            Assert.True(line.Length <= 76, $"line of {line.Length} characters");
        }
    }

    [Fact]
    public async Task A_display_name_containing_a_comma_does_not_break_the_from_header()
    {
        var handler = new CapturingHandler();

        await SendAsync(handler, senderName: "Ligature, Inc.");

        var mime = Decode(handler.LastBody!);

        // Encoded rather than quoted: an unencoded comma would make the address
        // parse as two, and Gmail rejects that with a 400.
        Assert.DoesNotContain("From: Ligature, Inc. <", mime, StringComparison.Ordinal);
        Assert.Contains("From: =?utf-8?B?", mime, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_subject_is_encoded_and_the_headers_are_crlf_terminated()
    {
        var handler = new CapturingHandler();

        await SendAsync(handler);

        var mime = Decode(handler.LastBody!);

        Assert.Contains("Subject: =?utf-8?B?", mime, StringComparison.Ordinal);
        Assert.Contains("Content-Transfer-Encoding: base64\r\n", mime, StringComparison.Ordinal);

        // Every line ending is CRLF: strip them all and no stray CR or LF is
        // left. A bare LF in a header block is what makes injection possible in
        // the first place.
        var stripped = mime.Replace("\r\n", string.Empty);

        Assert.DoesNotContain("\n", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_401_drops_the_cached_token_so_the_next_send_mints_a_fresh_one()
    {
        var handler = new CapturingHandler { SendStatus = HttpStatusCode.Unauthorized };

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(handler));

        // Without invalidation the dead token would be served until its nominal
        // expiry and EVERY activation mail would fail for up to an hour.
        var mintsBefore = handler.TokenMints;

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(handler, sameTransport: true));

        Assert.True(
            handler.TokenMints > mintsBefore,
            "a 401 must invalidate the cached token");
    }

    [Fact]
    public async Task A_500_does_not_drop_the_cached_token()
    {
        var handler = new CapturingHandler { SendStatus = HttpStatusCode.InternalServerError };

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(handler));

        var mintsBefore = handler.TokenMints;

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(handler, sameTransport: true));

        // A server error says nothing about the credential. Discarding a good
        // token on every transient failure would be a refresh storm.
        Assert.Equal(mintsBefore, handler.TokenMints);
    }

    // ------------------------------------------------------------------

    private GmailTransport? _transport;

    private async Task SendAsync(
        CapturingHandler handler,
        string recipient = "john.smith@example.com",
        string body = "open the link",
        string senderName = "Ligature",
        bool sameTransport = false)
    {
        if (!sameTransport || _transport is null)
        {
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var settings = new MailSettings(
                SenderAddress: "rdtech@example.com",
                SenderName: senderName,
                ServiceAccountEmail: "svc@project.iam.gserviceaccount.com",
                ServiceAccountPrivateKeyPem: TestKey.Pem,
                TransportTimeout: TimeSpan.FromSeconds(5));

            _transport = new GmailTransport(
                http,
                new GoogleServiceAccountTokenSource(http, settings, TimeProvider.System),
                settings);
        }

        await _transport.SendAsync(
            new RenderedMessage(recipient, "Activate your account", body),
            CancellationToken.None);
    }

    private static string Decode(string requestJson)
    {
        var raw = JsonDocument.Parse(requestJson).RootElement.GetProperty("raw").GetString()!;

        return Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(raw));
    }

    /// <summary>
    /// Answers the token endpoint and the send endpoint, counting mints so a
    /// test can see whether the cache was dropped.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        internal HttpStatusCode SendStatus { get; init; } = HttpStatusCode.OK;

        internal string? LastBody { get; private set; }

        internal int TokenMints { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                TokenMints++;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"access_token":"token-{{TokenMints}}","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(SendStatus)
            {
                Content = new StringContent(
                    """{"id":"gmail-message-id"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static class TestKey
    {
        internal static readonly string Pem = Create();

        private static string Create()
        {
            using var rsa = System.Security.Cryptography.RSA.Create(2048);

            return rsa.ExportPkcs8PrivateKeyPem();
        }
    }
}
