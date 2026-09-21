using System.Text.Json;
using SKSMCorp.Host.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// Which requests the Host accepts as coming from its own origin.
///
/// The rules under test, in order: safe methods pass; a non-blank
/// Sec-Fetch-Site decides alone and only "same-origin" passes; without it, an
/// absent Origin passes and a present one must be exactly one serialised
/// http(s) origin whose host and port equal the request's Host.
///
/// Every refusal is asserted in full — 403, the exact body and content type,
/// next NOT run, exactly one log entry — and every acceptance as next run with
/// nothing written and nothing logged. A test cannot pass by getting half of a
/// refusal right.
///
/// Pure: no database, no host.
/// </summary>
public sealed class CrossSiteMiddlewareTests
{
    private const string OwnHost = "app.example.test";

    private const string OwnOrigin = "https://app.example.test";

    private const string HostileOrigin = "https://evil.example";

    private const string RefusalMessage = "Cross-site requests are not accepted.";

    // ------------------------------------------------------------ methods

    /// <summary>
    /// Safe methods pass even with hostile evidence — which is only acceptable
    /// because a safe method never changes state.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task A_safe_method_is_allowed_whatever_its_provenance(string method)
    {
        var outcome = await RunAsync(method, request =>
        {
            request.Headers["Sec-Fetch-Site"] = "cross-site";
            request.Headers.Origin = HostileOrigin;
        });

        AssertAllowed(outcome);
    }

    /// <summary>
    /// An allowlist of safe methods: TRACE and methods nobody has heard of are
    /// checked like POST.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("TRACE")]
    [InlineData("PROPFIND")]
    public async Task A_cross_site_request_with_any_other_method_is_refused(string method)
    {
        var outcome = await RunAsync(
            method, request => request.Headers["Sec-Fetch-Site"] = "cross-site");

        AssertRefused(outcome);
    }

    /// <summary>
    /// Method names are case-sensitive (RFC 9110), so "get" is not GET and
    /// earns no exemption.
    /// </summary>
    [Fact]
    public async Task A_lowercase_get_is_not_a_safe_method()
    {
        var outcome = await RunAsync(
            "get", request => request.Headers["Sec-Fetch-Site"] = "cross-site");

        AssertRefused(outcome);
    }

    // ----------------------------------------------------- Sec-Fetch-Site

    [Fact]
    public async Task A_same_origin_request_is_allowed()
    {
        var outcome = await RunAsync(
            "POST", request => request.Headers["Sec-Fetch-Site"] = "same-origin");

        AssertAllowed(outcome);
    }

    /// <summary>
    /// "same-site" is the sibling-subdomain case SameSite=Strict cannot stop.
    /// "none" is refused by decision: no legitimate caller of this API sends a
    /// state-changing request from the address bar or a bookmark. Anything the
    /// header does not define exactly — another case, a joined value, junk — is
    /// refused rather than interpreted.
    /// </summary>
    [Theory]
    [InlineData("same-site")]
    [InlineData("cross-site")]
    [InlineData("none")]
    [InlineData("SAME-ORIGIN")]
    [InlineData("same-origin, same-origin")]
    [InlineData("garbage")]
    public async Task Any_sec_fetch_site_value_other_than_same_origin_is_refused(string value)
    {
        var outcome = await RunAsync(
            "POST", request => request.Headers["Sec-Fetch-Site"] = value);

        AssertRefused(outcome);
    }

    [Fact]
    public async Task Several_sec_fetch_site_values_are_refused_even_when_all_say_same_origin()
    {
        var outcome = await RunAsync(
            "POST",
            request => request.Headers["Sec-Fetch-Site"] =
                new StringValues(["same-origin", "same-origin"]));

        AssertRefused(outcome);
    }

    // --------------------------------------------------------- precedence

    /// <summary>
    /// Sec-Fetch-Site decides alone. A hostile Origin beside it is not
    /// consulted.
    /// </summary>
    [Fact]
    public async Task Same_origin_fetch_metadata_is_not_overruled_by_a_hostile_origin()
    {
        var outcome = await RunAsync("POST", request =>
        {
            request.Headers["Sec-Fetch-Site"] = "same-origin";
            request.Headers.Origin = HostileOrigin;
        });

        AssertAllowed(outcome);
    }

    /// <summary>
    /// The invariant: stronger evidence present is never rescued by weaker
    /// evidence. An Origin that matches does not turn a cross-site, "none" or
    /// unrecognised Sec-Fetch-Site into an acceptance.
    /// </summary>
    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    [InlineData("none")]
    [InlineData("garbage")]
    public async Task A_matching_origin_never_rescues_refusing_fetch_metadata(string value)
    {
        var outcome = await RunAsync("POST", request =>
        {
            request.Headers["Sec-Fetch-Site"] = value;
            request.Headers.Origin = OwnOrigin;
        });

        AssertRefused(outcome);
    }

    /// <summary>
    /// A blank Sec-Fetch-Site carries no evidence, so Origin decides — and it
    /// genuinely decides: the same blank header beside a hostile Origin is
    /// refused.
    /// </summary>
    [Theory]
    [InlineData("", OwnOrigin, true)]
    [InlineData("   ", OwnOrigin, true)]
    [InlineData("", HostileOrigin, false)]
    [InlineData("   ", HostileOrigin, false)]
    public async Task A_blank_sec_fetch_site_falls_to_the_origin_rule(
        string blank, string origin, bool allowed)
    {
        var outcome = await RunAsync("POST", request =>
        {
            request.Headers["Sec-Fetch-Site"] = blank;
            request.Headers.Origin = origin;
        });

        if (allowed)
            AssertAllowed(outcome);
        else
            AssertRefused(outcome);
    }

    // --------------------------------------------------- Origin fallback

    /// <summary>
    /// Neither header: not a browser that can be made to forge the request.
    /// This is what keeps test clients, scripts and bearer callers working.
    /// </summary>
    [Fact]
    public async Task A_request_with_neither_header_is_allowed()
    {
        AssertAllowed(await RunAsync("POST"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_origin_without_fetch_metadata_counts_as_absent(string blank)
    {
        var outcome = await RunAsync("POST", request => request.Headers.Origin = blank);

        AssertAllowed(outcome);
    }

    /// <summary>
    /// The last case is the accepted limitation, pinned so it cannot change
    /// silently: scheme is ignored, so an http origin on the same host passes.
    /// </summary>
    [Theory]
    [InlineData(OwnHost, "https://app.example.test")]
    [InlineData(OwnHost, "https://APP.Example.TEST")]
    [InlineData("app.example.test:8443", "https://app.example.test:8443")]
    [InlineData(OwnHost, "http://app.example.test")]
    public async Task An_origin_whose_host_and_port_match_the_request_host_is_allowed(
        string host, string origin)
    {
        var outcome = await RunAsync(
            "POST", request => request.Headers.Origin = origin, host);

        AssertAllowed(outcome);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("https://blog.example.test")]
    [InlineData("https://app.example.test:8443")]
    [InlineData("null")]
    [InlineData("not a url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://app.example.test")]
    [InlineData("https://app.example.test/")]
    [InlineData("https://app.example.test/path")]
    [InlineData("https://app.example.test?x=1")]
    [InlineData("https://user@app.example.test")]
    public async Task An_origin_that_is_not_exactly_our_own_is_refused(string origin)
    {
        var outcome = await RunAsync("POST", request => request.Headers.Origin = origin);

        AssertRefused(outcome);
    }

    [Fact]
    public async Task Several_origin_values_are_refused_even_when_all_match()
    {
        var outcome = await RunAsync(
            "POST",
            request => request.Headers.Origin = new StringValues([OwnOrigin, OwnOrigin]));

        AssertRefused(outcome);
    }

    [Fact]
    public async Task An_origin_is_refused_when_the_request_names_no_host()
    {
        var outcome = await RunAsync(
            "POST", request => request.Headers.Origin = OwnOrigin, host: null);

        AssertRefused(outcome);
    }

    // ------------------------------------------------------------ logging

    /// <summary>
    /// A refused request can still carry a live credential. The log names the
    /// method, the path and the two provenance headers, and neither the cookie
    /// nor the Authorization value appears in it — in the rendered message or in
    /// the structured state a logging provider might serialise instead.
    /// </summary>
    [Fact]
    public async Task A_refusal_is_logged_without_cookie_or_authorization_values()
    {
        const string CookieSecret = "cookie-secret-7f3a";
        const string BearerSecret = "bearer-secret-91c2";

        var outcome = await RunAsync("POST", request =>
        {
            request.Path = "/api/users";
            request.Headers["Sec-Fetch-Site"] = "same-site";
            request.Headers.Origin = "https://blog.example.test";
            request.Headers.Cookie = $"__Host-sksmcorp={CookieSecret}";
            request.Headers.Authorization = $"Bearer {BearerSecret}";
        });

        AssertRefused(outcome);

        var entry = Assert.Single(outcome.Log);

        Assert.Equal(LogLevel.Warning, entry.Level);

        foreach (var text in new[] { entry.Message, entry.State })
        {
            Assert.Contains("POST", text);
            Assert.Contains("/api/users", text);
            Assert.Contains("same-site", text);
            Assert.Contains("https://blog.example.test", text);

            Assert.DoesNotContain(CookieSecret, text);
            Assert.DoesNotContain(BearerSecret, text);
        }
    }

    [Fact]
    public async Task A_hostile_header_value_is_bounded_in_the_log()
    {
        var hostile = "https://" + new string('a', 5000) + ".example";

        var outcome = await RunAsync("POST", request => request.Headers.Origin = hostile);

        AssertRefused(outcome);

        var entry = Assert.Single(outcome.Log);

        Assert.DoesNotContain(hostile, entry.Message);
        Assert.True(entry.Message.Length < 1000, $"The log message was {entry.Message.Length} characters.");
    }

    // ------------------------------------------------------------ helpers

    private sealed record Outcome(
        bool NextRan,
        int StatusCode,
        string? ContentType,
        string Body,
        IReadOnlyList<LogEntry> Log);

    private sealed record LogEntry(LogLevel Level, string Message, string State);

    private static async Task<Outcome> RunAsync(
        string method,
        Action<HttpRequest>? present = null,
        string? host = OwnHost)
    {
        var logger = new CapturingLogger();

        var context = new DefaultHttpContext();

        context.Request.Method = method;
        context.Request.Path = "/api/users";

        if (host is not null)
            context.Request.Host = new HostString(host);

        context.Response.Body = new MemoryStream();

        present?.Invoke(context.Request);

        var nextRan = false;

        await new CrossSiteMiddleware(logger).InvokeAsync(context, _ =>
        {
            nextRan = true;

            return Task.CompletedTask;
        });

        context.Response.Body.Position = 0;

        using var reader = new StreamReader(context.Response.Body);

        return new Outcome(
            nextRan,
            context.Response.StatusCode,
            context.Response.ContentType,
            await reader.ReadToEndAsync(),
            logger.Entries);
    }

    private static void AssertAllowed(Outcome outcome)
    {
        Assert.True(outcome.NextRan, "An accepted request must reach the rest of the pipeline.");
        Assert.Equal(StatusCodes.Status200OK, outcome.StatusCode);
        Assert.Equal(string.Empty, outcome.Body);
        Assert.Empty(outcome.Log);
    }

    private static void AssertRefused(Outcome outcome)
    {
        Assert.False(outcome.NextRan, "A refused request must not reach the rest of the pipeline.");
        Assert.Equal(StatusCodes.Status403Forbidden, outcome.StatusCode);
        Assert.Equal("application/json; charset=utf-8", outcome.ContentType);

        using var document = JsonDocument.Parse(outcome.Body);

        var property = Assert.Single(document.RootElement.EnumerateObject());

        Assert.Equal("error", property.Name);
        Assert.Equal(RefusalMessage, property.Value.GetString());

        Assert.Single(outcome.Log);
    }

    /// <summary>
    /// Keeps both what a provider would render and the structured state it
    /// might serialise instead, so the redaction test checks both.
    /// </summary>
    private sealed class CapturingLogger : ILogger<CrossSiteMiddleware>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structured = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join("; ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                : state?.ToString() ?? string.Empty;

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), structured));
        }
    }
}
