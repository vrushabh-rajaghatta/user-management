using System.Net;
using System.Text.Json;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using SKSMCorp.Platform.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// Shared by the behaviour 11 suites (RateLimitEndpointTests,
/// TrustedProxyTests).
///
/// THE CLIENT ADDRESS. The test server gives every request NO connection
/// address, which is exactly the "no resolvable address" case the contract
/// keeps out of any shared bucket (RL-19). A test that needs an address sets it
/// here, on the connection, before the host's pipeline runs — the host/test
/// boundary, as the owner asked. Nothing in the host reads a test header.
///
/// THE PINNED ACCOUNT. One local account that can sign in, pinned (b11a…) for
/// the reason every host suite pins its actors: once it has signed in it is the
/// subject of audit records and cannot be removed. Its sessions, tokens and
/// failure state are reset before each use; the account itself stays.
/// </summary>
internal static class RateLimitHarness
{
    internal const string Refusal = """{"error":"Too many attempts. Try again later."}""";

    internal const string Password = "correct-horse-battery-staple";

    internal static readonly Guid SignerUser = Guid.Parse("b11a0000-0000-4000-8000-000000000001");

    internal static readonly Guid SignerIdentity = Guid.Parse("b11a0000-0000-4000-8000-000000000002");

    internal const string SignerUsername = "permanent-rate-limit-signer";

    internal const string SignerEmail = "permanent-rate-limit-signer@example.test";

    /// <summary>A value nobody has used before, so audit counts start at zero.</summary>
    internal static string Fresh(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    internal sealed record Response(HttpStatusCode Status, string Body, string? RetryAfter);

    /// <summary>
    /// A JSON POST from a chosen connection address, optionally with an
    /// X-Forwarded-For header and a bearer carrier.
    /// </summary>
    internal static async Task<Response> PostAsync(
        HostFactory factory,
        string path,
        object body,
        string? peer = null,
        string? forwardedFor = null,
        string? bearer = null)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(body);

        var context = await factory.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Post;
            c.Request.Scheme = "http";
            c.Request.Host = new HostString("localhost");
            c.Request.Path = path;
            c.Request.ContentType = "application/json";
            c.Request.ContentLength = json.Length;
            c.Request.Body = new MemoryStream(json);

            // The test server decides "has a body" from how IT was given one,
            // and minimal APIs skip binding when told there is none.
            c.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());

            if (peer is not null)
                c.Connection.RemoteIpAddress = IPAddress.Parse(peer);

            if (forwardedFor is not null)
                c.Request.Headers["X-Forwarded-For"] = forwardedFor;

            if (bearer is not null)
                c.Request.Headers.Authorization = $"Bearer {bearer}";
        });

        using var reader = new StreamReader(context.Response.Body);
        var text = await reader.ReadToEndAsync();

        var retryAfter = context.Response.Headers.RetryAfter;

        return new Response(
            (HttpStatusCode)context.Response.StatusCode,
            text,
            retryAfter.Count == 0 ? null : retryAfter.ToString());
    }

    private sealed class HasBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    internal static Task<Response> SignInAsync(
        HostFactory factory, string username, string password = "wrong-password-entirely",
        string? peer = null, string? forwardedFor = null, string? bearer = null)
        => PostAsync(factory, "/api/auth/sign-in", new { Username = username, Password = password },
            peer, forwardedFor, bearer);

    internal static Task<Response> RequestResetAsync(
        HostFactory factory, string emailOrUsername, string? peer = null, string? forwardedFor = null)
        => PostAsync(factory, "/api/account/password-reset-request",
            new { EmailOrUsername = emailOrUsername }, peer, forwardedFor);

    internal static Task<Response> ResetPasswordAsync(
        HostFactory factory, string token, string? peer = null)
        => PostAsync(factory, "/api/account/reset-password",
            new { Token = token, NewPassword = Password }, peer);

    internal static Task<Response> ActivateAsync(
        HostFactory factory, string token, string? peer = null)
        => PostAsync(factory, "/api/account/activate",
            new { Token = token, NewPassword = Password }, peer);

    /// <summary>The carrier a 204 sign-in set, from its Set-Cookie header.</summary>
    internal static async Task<string> SignInForCarrierAsync(HostFactory factory)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/auth/sign-in",
            System.Net.Http.Json.JsonContent.Create(new { Username = SignerUsername, Password }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        return IssuedCarrier.From(response);
    }

    // ------------------------------------------------------ the pinned account

    /// <summary>
    /// Creates the signer once (activated through the real endpoint, so the
    /// credential is the real one), and on every call returns it to a clean
    /// state: no sessions, no failures, no lock.
    ///
    /// Open tokens are left alone. A reset request's notification references
    /// its token, so the token cannot be deleted; and each new reset request
    /// supersedes the earlier ones anyway (UT5).
    /// </summary>
    internal static async Task EnsureSignerAsync()
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using (var context = CreateContext())
        {
            if (!await context.Set<User>().AnyAsync(x => x.Id == new UserId(SignerUser)))
            {
                var now = DateTimeOffset.UtcNow;

                var user = User.CreateHuman(
                    new UserId(SignerUser), "Rate", "Limit", "Rate Limit Signer",
                    SignerEmail, now, User.SystemUserId);

                var identity = UserIdentity.CreateLocal(
                    new UserIdentityId(SignerIdentity), user.Id, ActorType.Human,
                    SignerUsername, now, User.SystemUserId);

                var tokenId = UserTokenId.New();
                var material = new UserTokenService().Generate(tokenId);

                context.AddRange(
                    user,
                    identity,
                    UserToken.Create(
                        tokenId, identity.Id, TokenType.Activation, material.Hash,
                        now, now.AddHours(72), User.SystemUserId));

                await context.SaveChangesAsync(CancellationToken.None);

                await using var factory = new HostFactory();

                var activated = await ActivateAsync(factory, material.PlainText);

                Assert.Equal(HttpStatusCode.OK, activated.Status);
            }
        }

        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM user_session WHERE user_identity_id = @id",
            "UPDATE credential SET failed_attempt_count = 0, locked_until = NULL WHERE user_identity_id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", SignerIdentity);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// An open token of the given type for the signer, and its plaintext. Any
    /// earlier open token of that type is invalidated first, as a real issue
    /// would (UT5), because only one may be open per type.
    /// </summary>
    internal static async Task<(Guid TokenId, string PlainText)> IssueSignerTokenAsync(TokenType type)
    {
        await using (var connection = await TestDatabase.OpenAsync())
        {
            await using var invalidate = new NpgsqlCommand(
                """
                UPDATE user_token SET invalidated_at = now()
                WHERE user_identity_id = @id AND token_type = @type
                  AND used_at IS NULL AND invalidated_at IS NULL
                """, connection);

            invalidate.Parameters.AddWithValue("id", SignerIdentity);
            invalidate.Parameters.AddWithValue("type", type.ToString());
            await invalidate.ExecuteNonQueryAsync();
        }

        var now = DateTimeOffset.UtcNow;
        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        await using var context = CreateContext();

        context.Add(UserToken.Create(
            tokenId, new UserIdentityId(SignerIdentity), type, material.Hash,
            now, now.AddHours(1), User.SystemUserId));

        await context.SaveChangesAsync(CancellationToken.None);

        return (tokenId.Value, material.PlainText);
    }

    // ------------------------------------------------------------ read-backs

    internal static Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
        => ScalarAsync<long>(sql, parameters);

    internal static async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        var result = await command.ExecuteScalarAsync();

        return result is null or DBNull ? default! : (T)Convert.ChangeType(result, typeof(T));
    }

    internal static Task<long> SignInFailedCountAsync(string attemptedIdentifier)
        => ScalarAsync(
            """
            SELECT count(*) FROM audit.audit_record
            WHERE event_type = 'SignInFailed' AND payload ->> 'AttemptedIdentifier' = @identifier
            """,
            ("identifier", attemptedIdentifier));

    /// <summary>The IpAddress the one SignInFailed for this identifier recorded.</summary>
    internal static Task<string?> SignInFailedAddressAsync(string attemptedIdentifier)
        => ScalarAsync<string?>(
            """
            SELECT payload ->> 'IpAddress' FROM audit.audit_record
            WHERE event_type = 'SignInFailed' AND payload ->> 'AttemptedIdentifier' = @identifier
            ORDER BY sequence DESC LIMIT 1
            """,
            ("identifier", attemptedIdentifier));

    internal static Task<long> SignerEventCountAsync(string eventType)
        => ScalarAsync(
            """
            SELECT count(*) FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = @type AND e.entity_id = @identity
            """,
            ("type", eventType), ("identity", SignerIdentity));

    internal static Task<long> SignerSessionCountAsync()
        => ScalarAsync(
            "SELECT count(*) FROM user_session WHERE user_identity_id = @id",
            ("id", SignerIdentity));

    internal static async Task<(int FailedAttempts, bool Locked)> SignerCredentialAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT failed_attempt_count, locked_until IS NOT NULL FROM credential WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", SignerIdentity);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return (reader.GetInt32(0), reader.GetBoolean(1));
    }

    internal static Task<bool> TokenUsedAsync(Guid tokenId)
        => ScalarAsync<bool>(
            "SELECT used_at IS NOT NULL FROM user_token WHERE id = @id", ("id", tokenId));

    private static SKSMCorpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SKSMCorpDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options;

        return new SKSMCorpDbContext(options);
    }

    // ------------------------------------------------------------- logging

    /// <summary>Every log entry the host writes, for RL-16.</summary>
    internal sealed class CapturedLogs : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Category, string Message)> _entries = [];

        internal IReadOnlyList<(LogLevel Level, string Category, string Message)> Entries
        {
            get
            {
                lock (_entries)
                    return _entries.ToList();
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturedLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries)
                    owner._entries.Add((logLevel, category, formatter(state, exception)));
            }
        }
    }
}
