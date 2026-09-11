using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Ligature.Platform.Application.Notifications;

namespace Ligature.Platform.Persistence.Notifications;

/// <summary>
/// Sends through the Gmail API. The only type in the system that knows Google
/// exists.
///
/// Everything provider-specific stops here: endpoints, OAuth, the JWT
/// assertion, the credential, HTTP. Nothing in the Application assembly
/// references any of it, so replacing this with a Microsoft implementation is a
/// second class behind INotificationTransport and a configuration change.
///
/// ACCEPTED RESIDUAL EXPOSURE. users.messages.send writes a copy of the message
/// into the sending account's Sent folder, and that copy contains the
/// activation URL and therefore the token plaintext. There is no flag to
/// suppress it. Anyone with access to the sending mailbox can read live
/// activation links until they expire, which bounds the exposure at the token
/// lifetime. This was weighed against an SMTP relay — which leaves no Sent copy
/// but authenticates with an account-wide app password rather than a send-only
/// scope — and accepted deliberately: the credential boundary was judged the
/// stronger control. It is recorded here so nobody later mistakes it for an
/// oversight.
///
/// On any failure this throws, and the sender records NotSent/TransportFailed.
/// There is no retry here or anywhere: a retry needs the plaintext, and keeping
/// the plaintext long enough to retry is keeping it.
/// </summary>
internal sealed class GmailTransport : INotificationTransport, IDisposable
{
    private const string SendEndpoint =
        "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";

    private readonly HttpClient _http;
    private readonly GoogleServiceAccountTokenSource _tokens;
    private readonly MailSettings _settings;

    internal GmailTransport(
        HttpClient http,
        GoogleServiceAccountTokenSource tokens,
        MailSettings settings)
    {
        _http = http;
        _tokens = tokens;
        _settings = settings;
    }

    public async Task<string?> SendAsync(
        RenderedMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var token = await _tokens.GetAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
        {
            Content = JsonContent.Create(
                new SendRequest(Base64Url.EncodeToString(Compose(message)))),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, cancellationToken);

        // The body is deliberately not quoted. It is the request we just sent
        // being echoed back on some error shapes, and that request contains the
        // token.
        if (!response.IsSuccessStatusCode)
        {
            // A 401 says the credential, not the message, was rejected. Drop
            // the cached token so the NEXT send mints a fresh one — this send
            // still fails, deliberately: re-sending here would be a second
            // delivery attempt, and the design permits none.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                _tokens.Invalidate(token);

            throw new InvalidOperationException(
                $"Gmail refused the message ({(int)response.StatusCode}).");
        }

        var sent = await response.Content
            .ReadFromJsonAsync<SendResponse>(cancellationToken);

        return sent?.Id;
    }

    /// <summary>
    /// An RFC 5322 message.
    ///
    /// HEADER INJECTION IS REFUSED HERE, before anything is composed. An
    /// address is interpolated straight into a header, and the CRLF
    /// normalisation below would turn an embedded newline into a WELL-FORMED
    /// injected header rather than corrupting it into harmlessness — so a
    /// recipient carrying a control character could add headers or terminate
    /// the header block and replace the body. EmailAddress does not currently
    /// reject control characters, and the same value reaches CRD-C2 from an
    /// anonymous caller in slice N2, so this boundary fails closed rather than
    /// trusting its input.
    ///
    /// Every control character is refused, not merely CR and LF: the guard
    /// should not depend on which characters today's formatter happens to treat
    /// as significant.
    ///
    /// The body is base64 encoded and WRAPPED at 76 characters (RFC 2045 §6.8);
    /// RFC 5322 §2.1.1 caps any line at 998 octets, and a fold inserted by a
    /// downstream agent inside an unwrapped base64 run would corrupt it
    /// silently. The subject and the display name are RFC 2047 encoded
    /// unconditionally — conditional encoding is how a future non-ASCII value
    /// becomes a bug, and encoding the display name also removes any question
    /// of quoting a name containing a comma or a full stop.
    /// </summary>
    private byte[] Compose(RenderedMessage message)
    {
        var recipient = HeaderSafe(message.Recipient, nameof(message.Recipient));
        var sender = HeaderSafe(_settings.SenderAddress, "sender address");

        var mime =
            $"""
             From: {EncodedWord(_settings.SenderName)} <{sender}>
             To: {recipient}
             Subject: {EncodedWord(message.Subject)}
             MIME-Version: 1.0
             Content-Type: text/plain; charset=utf-8
             Content-Transfer-Encoding: base64

             {Wrapped(message.Body)}
             """;

        return Encoding.UTF8.GetBytes(mime.ReplaceLineEndings("\r\n"));
    }

    /// <summary>
    /// Refuses any control character in a value that is interpolated raw into a
    /// header. Throwing means the message is never constructed, the send
    /// becomes NotSent/TransportFailed, and nothing is mailed on behalf of the
    /// injected headers.
    /// </summary>
    private static string HeaderSafe(string value, string what)
    {
        foreach (var character in value)
        {
            if (!char.IsControl(character))
                continue;

            throw new InvalidOperationException(
                $"Notification defect — the {what} contains a control "
                + "character and cannot be placed in a mail header. The value "
                + "is deliberately not quoted here.");
        }

        return value;
    }

    /// <summary>RFC 2047 encoded-word. Safe in both a subject and a display name.</summary>
    private static string EncodedWord(string value)
        => $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";

    /// <summary>Base64, folded at the RFC 2045 line limit.</summary>
    private static string Wrapped(string body)
        => Convert.ToBase64String(
            Encoding.UTF8.GetBytes(body),
            Base64FormattingOptions.InsertLineBreaks);

    public void Dispose() => _tokens.Dispose();

    private sealed record SendRequest(
        [property: JsonPropertyName("raw")] string Raw);

    private sealed record SendResponse(
        [property: JsonPropertyName("id")] string? Id);
}
