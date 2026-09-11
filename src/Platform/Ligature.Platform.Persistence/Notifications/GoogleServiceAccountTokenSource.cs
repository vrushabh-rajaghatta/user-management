using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ligature.Platform.Persistence.Notifications;

/// <summary>
/// Mints Google access tokens from a service account, by hand.
///
/// WHY NOT THE SDK. Google.Apis.Gmail pulls the whole Google.Apis stack and
/// Newtonsoft.Json into a solution that has six packages and uses
/// System.Text.Json. The flow it would provide is two HTTP calls and an RS256
/// signature, all of which the BCL already does. Everything here is documented
/// protocol, not reverse engineering.
///
/// THE FLOW. Build a JWT asserting "this service account, acting as this user,
/// wants this scope", sign it with the service account's private key, and
/// exchange it at Google's token endpoint for a bearer token. With domain-wide
/// delegation the `sub` claim is the mailbox being impersonated, which a
/// Workspace administrator must have authorised for THIS client id and ONLY the
/// gmail.send scope.
///
/// The scope is the credential boundary that matters: gmail.send cannot read a
/// mailbox, cannot list messages, and cannot administer anything. That is what
/// §8.2's "send-only, no mailbox read" means in practice, and it is why this is
/// preferred over an app password, which would be account-wide.
///
/// Tokens are cached until shortly before expiry and refreshed under a lock, so
/// four consumers sending at once mint one token rather than four.
/// </summary>
internal sealed class GoogleServiceAccountTokenSource : IDisposable
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string SendScope = "https://www.googleapis.com/auth/gmail.send";
    private const string JwtBearerGrant = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    /// <summary>
    /// Refresh this long before the token actually expires, so a send that
    /// starts just under the wire does not arrive just over it.
    /// </summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan AssertionLifetime = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly MailSettings _settings;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refresh = new(1, 1);

    /// <summary>
    /// ONE reference, published atomically. The previous shape held the token
    /// and its expiry in two fields written under the lock but read outside it,
    /// which is not safe: DateTimeOffset is a multi-field struct and can tear,
    /// and without a volatile write the two stores may be observed out of order
    /// on a weak memory model such as ARM64 — yielding the new expiry with the
    /// old token, and so a send with a credential that has already expired.
    ///
    /// A single immutable record behind Volatile.Read/Write removes both: a
    /// reader sees either the whole old value or the whole new one.
    /// </summary>
    private CachedToken? _cached;

    internal GoogleServiceAccountTokenSource(
        HttpClient http,
        MailSettings settings,
        TimeProvider time)
    {
        _http = http;
        _settings = settings;
        _time = time;
    }

    internal async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        if (Usable(Volatile.Read(ref _cached)) is { } live)
            return live;

        await _refresh.WaitAsync(cancellationToken);

        try
        {
            // Re-checked inside the lock: whoever was ahead of us has already
            // refreshed it, so four consumers sending at once mint one token
            // rather than four.
            if (Usable(Volatile.Read(ref _cached)) is { } refreshed)
                return refreshed;

            var issued = await ExchangeAsync(cancellationToken);

            var minted = new CachedToken(
                issued.AccessToken,
                _time.GetUtcNow().AddSeconds(issued.ExpiresIn));

            Volatile.Write(ref _cached, minted);

            return minted.Value;
        }
        finally
        {
            _refresh.Release();
        }
    }

    /// <summary>
    /// Drops the cached token if it is still the one that just failed.
    ///
    /// A 401 is the signal that the credential or the delegation may no longer
    /// be valid — a rotated key, a revoked grant, a withdrawn scope. Without
    /// this the dead token would be served until its nominal expiry and EVERY
    /// activation mail would fail for up to an hour with no self-recovery.
    ///
    /// Compare-and-clear rather than unconditional clear, so that several
    /// consumers failing at once do not each discard a good token that another
    /// has already minted — which would turn one revocation into a refresh
    /// storm.
    /// </summary>
    internal void Invalidate(string staleToken)
    {
        var cached = Volatile.Read(ref _cached);

        if (cached is not null && string.Equals(cached.Value, staleToken, StringComparison.Ordinal))
            Volatile.Write(ref _cached, null);
    }

    private string? Usable(CachedToken? cached)
        => cached is not null && _time.GetUtcNow() < cached.ExpiresAt - RefreshMargin
            ? cached.Value
            : null;

    private async Task<TokenResponse> ExchangeAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = JwtBearerGrant,
                ["assertion"] = BuildAssertion(),
            });

        using var response = await _http.PostAsync(
            TokenEndpoint, content, cancellationToken);

        // Deliberately does not include the response body in the message. A
        // token endpoint's error body is not secret, but this exception travels
        // into a transport failure path, and a habit of echoing provider bodies
        // is how secrets reach logs.
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Google refused the service account assertion "
                + $"({(int)response.StatusCode}). Check that the Workspace "
                + "administrator has authorised this client id for the "
                + "gmail.send scope and that the impersonated address is "
                + "correct.");
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException(
                "Google returned no token payload.");
    }

    private string BuildAssertion()
    {
        var now = _time.GetUtcNow();

        var header = Segment(new { alg = "RS256", typ = "JWT" });

        var claims = Segment(new
        {
            iss = _settings.ServiceAccountEmail,
            // Domain-wide delegation: act as this mailbox.
            sub = _settings.SenderAddress,
            scope = SendScope,
            aud = TokenEndpoint,
            iat = now.ToUnixTimeSeconds(),
            exp = now.Add(AssertionLifetime).ToUnixTimeSeconds(),
        });

        var unsigned = $"{header}.{claims}";

        using var rsa = RSA.Create();

        rsa.ImportFromPem(_settings.ServiceAccountPrivateKeyPem);

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(unsigned),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{unsigned}.{Base64Url.EncodeToString(signature)}";
    }

    private static string Segment<T>(T value)
        => Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(value));

    public void Dispose() => _refresh.Dispose();

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
