using SKSMCorp.Host.Authentication;
using SKSMCorp.Host.Configuration;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace SKSMCorp.Host.Tests;

/// <summary>
/// Which credential a request presents, and therefore which session — if any —
/// the Host asks the platform to establish.
///
/// The locked rule under test: the transport is chosen by PRESENCE before
/// anything is interpreted. A non-blank Authorization header claims the
/// request, whatever it contains, and the cookie is then never consulted;
/// without one, only the carrier cookie is read, and only when it appears
/// exactly once under exactly its name.
///
/// Every test observes the same two facts — the SessionId the platform was
/// asked to establish, and the SessionId recorded on CurrentCarrier — and the
/// shared helper asserts they agree and that next ran, because this middleware
/// never rejects a request. The real AccessCarrier verifies; only the
/// establisher, which would need a database, is replaced by a recorder.
///
/// Pure: no database, no host.
/// </summary>
public sealed class CallerMiddlewareTests
{
    private const string PrimaryKey =
        "TGlnYXR1cmUgSG9zdCB0ZXN0IHNpZ25pbmcga2V5IDE=";

    /// <summary>A valid key the verifier has never been configured with.</summary>
    private const string ForeignKey =
        "TGlnYXR1cmUgSG9zdCB0ZXN0IHNpZ25pbmcga2V5IDM=";

    /// <summary>
    /// The contract name, as a literal: a change to CarrierCookie.Name should
    /// fail here rather than follow along.
    /// </summary>
    private const string CookieName = "__Host-sksmcorp";

    private static readonly AccessCarrier Verifier =
        Carrier("v1", ("SKSMCORP_SIGNING_KEY_V1", PrimaryKey));

    private static readonly UserSessionId SessionA = UserSessionId.New();

    private static readonly UserSessionId SessionB = UserSessionId.New();

    private static readonly string CarrierA = Verifier.Issue(SessionA);

    private static readonly string CarrierB = Verifier.Issue(SessionB);

    private static readonly string ForgedA = Tamper(CarrierA);

    private static readonly string UnknownKeyA =
        Carrier("v3", ("SKSMCORP_SIGNING_KEY_V3", ForeignKey)).Issue(SessionA);

    // ------------------------------------------------------------- source

    [Fact]
    public async Task A_valid_bearer_carrier_establishes_its_session()
    {
        var established = await EstablishedBy(
            request => request.Headers.Authorization = $"Bearer {CarrierA}");

        Assert.Equal(SessionA, established);
    }

    [Fact]
    public async Task A_valid_carrier_cookie_establishes_its_session()
    {
        var established = await EstablishedBy(
            request => request.Headers.Cookie = $"{CookieName}={CarrierB}");

        Assert.Equal(SessionB, established);
    }

    [Fact]
    public async Task A_request_presenting_nothing_establishes_nothing()
    {
        Assert.Null(await EstablishedBy(_ => { }));
    }

    /// <summary>
    /// RFC 7235 schemes are case-insensitive. Pinned so the precedence rewrite
    /// cannot quietly narrow what a valid Bearer header is.
    /// </summary>
    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    public async Task The_bearer_scheme_is_matched_case_insensitively(string scheme)
    {
        var established = await EstablishedBy(
            request => request.Headers.Authorization = $"{scheme} {CarrierA}");

        Assert.Equal(SessionA, established);
    }

    // --------------------------------------------------------- precedence

    [Fact]
    public async Task The_authorization_header_wins_over_a_valid_cookie()
    {
        var established = await EstablishedBy(request =>
        {
            request.Headers.Authorization = $"Bearer {CarrierA}";
            request.Headers.Cookie = $"{CookieName}={CarrierB}";
        });

        Assert.Equal(SessionA, established);
    }

    /// <summary>
    /// The heart of the rule. Every one of these headers is unusable, the
    /// cookie beside it is valid, and the answer must still be no caller.
    ///
    /// "a bare valid carrier with no scheme" is the sharpest case: the value in
    /// the header would verify if it were read as a carrier, and the cookie
    /// would verify too. Inferring intent from the scheme would rescue it.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnusableAuthorizationHeaders))]
    public async Task A_present_but_unusable_authorization_header_never_falls_back_to_the_cookie(
        string label, string authorization)
    {
        _ = label;

        var established = await EstablishedBy(request =>
        {
            request.Headers.Authorization = authorization;
            request.Headers.Cookie = $"{CookieName}={CarrierB}";
        });

        Assert.Null(established);
    }

    public static TheoryData<string, string> UnusableAuthorizationHeaders => new()
    {
        { "a forged bearer carrier", $"Bearer {ForgedA}" },
        { "a bearer carrier signed by an unknown key", $"Bearer {UnknownKeyA}" },
        { "the bearer scheme with no parameter", "Bearer" },
        { "a bearer parameter containing whitespace", "Bearer a b" },
        { "a bare valid carrier with no scheme", CarrierB },
        { "the basic scheme", "Basic dXNlcjpwYXNz" },
        { "the negotiate scheme", "Negotiate YIIGhgYJKoZIhvcSAQICAQBuggZ1" },
    };

    /// <summary>
    /// Two credentials in one request are not a choice this code makes — even
    /// when both are valid, and even when the cookie is too.
    /// </summary>
    [Fact]
    public async Task Two_authorization_values_establish_nothing_and_do_not_fall_back()
    {
        var established = await EstablishedBy(request =>
        {
            request.Headers.Authorization = new StringValues(
                [$"Bearer {CarrierA}", $"Bearer {CarrierB}"]);
            request.Headers.Cookie = $"{CookieName}={CarrierB}";
        });

        Assert.Null(established);
    }

    /// <summary>
    /// A blank header carries no credential, so the cookie is read. Nothing is
    /// rescued: there was no explicit credential in the first place.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_authorization_header_counts_as_absent(string blank)
    {
        var established = await EstablishedBy(request =>
        {
            request.Headers.Authorization = blank;
            request.Headers.Cookie = $"{CookieName}={CarrierB}";
        });

        Assert.Equal(SessionB, established);
    }

    /// <summary>
    /// Once the header claims the request, the state of the cookie is
    /// irrelevant — a cookie presentation that would itself establish nothing
    /// changes nothing about a valid header.
    /// </summary>
    [Fact]
    public async Task An_unusable_cookie_does_not_affect_a_request_that_presents_a_header()
    {
        var established = await EstablishedBy(request =>
        {
            request.Headers.Authorization = $"Bearer {CarrierA}";
            request.Headers.Cookie = $"{CookieName}={CarrierB}; {CookieName}={CarrierB}";
        });

        Assert.Equal(SessionA, established);
    }

    // ------------------------------------------------------- cookie forms

    [Theory]
    [MemberData(nameof(UnusableCookieValues))]
    public async Task An_unusable_carrier_cookie_establishes_nothing(
        string label, string value)
    {
        _ = label;

        var established = await EstablishedBy(
            request => request.Headers.Cookie = $"{CookieName}={value}");

        Assert.Null(established);
    }

    public static TheoryData<string, string> UnusableCookieValues => new()
    {
        { "a malformed value", "not-a-carrier" },
        { "a forged carrier", ForgedA },
        { "a carrier signed by an unknown key", UnknownKeyA },
        { "an empty value", "" },
        { "a quoted carrier", $"\"{CarrierA}\"" },
    };

    /// <summary>
    /// Exactly once, whether or not the copies agree: the rule is about what
    /// was presented. Request.Cookies would silently return the last one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_carrier_cookie_presented_twice_establishes_nothing(bool identical)
    {
        var second = identical ? CarrierA : CarrierB;

        var established = await EstablishedBy(
            request => request.Headers.Cookie =
                $"{CookieName}={CarrierA}; {CookieName}={second}");

        Assert.Null(established);
    }

    /// <summary>
    /// Request.Cookies matches names case-insensitively, so it would return
    /// this as the carrier cookie. It is not the cookie the Host wrote.
    /// </summary>
    [Fact]
    public async Task A_case_variant_of_the_cookie_name_is_not_the_carrier_cookie()
    {
        var established = await EstablishedBy(
            request => request.Headers.Cookie = $"__host-sksmcorp={CarrierA}");

        Assert.Null(established);
    }

    /// <summary>
    /// The planted variant comes LAST, which is the order in which
    /// Request.Cookies would have preferred it over the real cookie.
    /// </summary>
    [Fact]
    public async Task A_case_variant_beside_the_real_cookie_is_ignored()
    {
        var established = await EstablishedBy(
            request => request.Headers.Cookie =
                $"{CookieName}={CarrierB}; __host-sksmcorp={CarrierA}");

        Assert.Equal(SessionB, established);
    }

    [Fact]
    public async Task A_cookie_under_another_name_is_not_the_carrier_cookie()
    {
        var established = await EstablishedBy(
            request => request.Headers.Cookie = $"sksmcorp={CarrierA}");

        Assert.Null(established);
    }

    /// <summary>
    /// Another application's broken cookie on the same host must not sign
    /// anybody out. The strict parser would reject the whole header here.
    /// </summary>
    [Fact]
    public async Task An_unrelated_malformed_cookie_does_not_block_the_carrier_cookie()
    {
        var established = await EstablishedBy(
            request => request.Headers.Cookie = $"bad cookie=x y; {CookieName}={CarrierB}");

        Assert.Equal(SessionB, established);
    }

    /// <summary>
    /// HTTP/2 may split cookies across several Cookie header fields.
    /// </summary>
    [Fact]
    public async Task The_carrier_cookie_is_found_when_cookies_arrive_in_separate_headers()
    {
        var established = await EstablishedBy(
            request => request.Headers.Cookie = new StringValues(
                ["theme=dark", $"{CookieName}={CarrierB}"]));

        Assert.Equal(SessionB, established);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>
    /// Runs the real middleware over a request shaped by the test, and returns
    /// the session it established, or null.
    ///
    /// Three invariants are asserted on every call, so no test can pass while
    /// breaking them: next always runs, at most one session is ever handed to
    /// the platform, and the session recorded on CurrentCarrier is the one the
    /// platform was asked about.
    /// </summary>
    private static async Task<UserSessionId?> EstablishedBy(Action<HttpRequest> present)
    {
        var establisher = new RecordingEstablisher();

        await using var provider = new ServiceCollection()
            .AddScoped<CurrentCarrier>()
            .AddSingleton<ICallerEstablisher>(establisher)
            .BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };

        present(context.Request);

        var nextRan = false;

        await new CallerMiddleware(Verifier).InvokeAsync(context, _ =>
        {
            nextRan = true;

            return Task.CompletedTask;
        });

        Assert.True(nextRan, "CallerMiddleware must always call next; it never rejects.");
        Assert.True(establisher.Received.Count <= 1, "More than one session was presented to the platform.");

        var recorded = scope.ServiceProvider.GetRequiredService<CurrentCarrier>().SessionId;

        Assert.Equal(establisher.Received.SingleOrDefault(), recorded);

        return recorded;
    }

    private static AccessCarrier Carrier(
        string currentKeyId, params (string Key, string Value)[] keys)
        => new(
            SigningKeyRing.Load(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        keys
                            .Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))
                            .Append(new KeyValuePair<string, string?>(
                                SigningKeyRing.CurrentKeySetting, currentKeyId)))
                    .Build()));

    /// <summary>Changes one character of the signature component only.</summary>
    private static string Tamper(string carrier)
    {
        var components = carrier.Split('.');
        var signature = components[2].ToCharArray();

        signature[0] = signature[0] == 'A' ? 'B' : 'A';
        components[2] = new string(signature);

        return string.Join('.', components);
    }

    /// <summary>
    /// Stands in for the platform's per-request session check, which needs a
    /// database. It records what it was asked and establishes whatever that
    /// is: whether a session is live is not this middleware's question.
    /// </summary>
    private sealed class RecordingEstablisher : ICallerEstablisher
    {
        public List<UserSessionId> Received { get; } = [];

        public Task<bool> EstablishAsync(
            UserSessionId sessionId, CancellationToken cancellationToken)
        {
            Received.Add(sessionId);

            return Task.FromResult(true);
        }
    }
}
