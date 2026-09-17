using Ligature.Host.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Host.Tests;

/// <summary>
/// Runs the real host — real middleware, real routing, real model binding, real
/// command pipeline, real PostgreSQL. Only the configuration is supplied from
/// here, and it is supplied the same way a deployment would supply it.
///
/// Nothing is stubbed. The point of this suite is the seam between HTTP and the
/// platform, and a factory that replaced the persistence registrations would
/// prove the seam works against something that is not the system.
/// </summary>
internal sealed class HostFactory : WebApplicationFactory<Program>
{
    private readonly string? _apiDocumentation;

    private readonly Action<IServiceCollection>? _services;

    /// <summary>
    /// The default leaves LIGATURE_API_DOCUMENTATION UNSET, which is what makes
    /// the rest of the suite evidence that the documentation surface is absent
    /// unless something asks for it. Tests that want it pass the raw setting
    /// value — including a deliberately invalid one, since refusing to start on
    /// a malformed value is part of the contract.
    /// </summary>
    /// <param name="services">
    /// Registrations applied to the REAL host's service collection, for tests
    /// that must prove what Program.cs does with a registration it did not
    /// make itself — a refused start-up in particular.
    /// </param>
    internal HostFactory(
        string? apiDocumentation = null,
        Action<IServiceCollection>? services = null)
    {
        _apiDocumentation = apiDocumentation;
        _services = services;

        // No ambient cookie state. The default client authenticates with a
        // bearer carrier and nothing else; were it to keep the cookie sign-in
        // sets, a second sign-in on the same client would present a live
        // session and be refused, and unrelated tests would acquire state from
        // whichever sign-in ran before them. Browser semantics are opted into
        // explicitly, through CreateBrowser.
        ClientOptions.HandleCookies = false;
    }

    /// <summary>
    /// A client that behaves as a browser does with the carrier cookie: HTTPS,
    /// so the Secure cookie is replayed, and a cookie container that stores
    /// what Set-Cookie says and applies deletions. The container is returned
    /// so a test can see what the client was left holding.
    ///
    /// It is not a browser. It enforces neither the __Host- prefix nor
    /// SameSite, and sends no Origin or Sec-Fetch-Site; CarrierCookieTests and
    /// the cross-site tests cover those.
    /// </summary>
    internal (HttpClient Client, System.Net.CookieContainer Jar) CreateBrowser()
    {
        var jar = new System.Net.CookieContainer();

        var client = CreateDefaultClient(
            BrowserAddress,
            new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(jar));

        return (client, jar);
    }

    internal static readonly Uri BrowserAddress = new("https://localhost");

    /// <summary>
    /// Thirty-two bytes exactly — the section 17 minimum — so the tests run
    /// against the smallest key the host will accept rather than a comfortable
    /// one. A test key in a repository is not a secret: the host has no default
    /// key precisely so that this value can never become one.
    /// </summary>
    internal const string PrimaryKey =
        "TGlnYXR1cmUgSG9zdCB0ZXN0IHNpZ25pbmcga2V5IDE=";

    /// <summary>
    /// A second configured key, used to prove that a carrier signed by one key
    /// is not accepted under another identifier.
    /// </summary>
    internal const string SecondaryKey =
        "TGlnYXR1cmUgSG9zdCB0ZXN0IHNpZ25pbmcga2V5IDI=";

    internal const string PrimaryKeyId = "v1";

    internal const string SecondaryKeyId = "v2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting(
            HostConfiguration.ConnectionSetting, TestDatabase.ConnectionString);

        builder.UseSetting(SigningKeyRing.CurrentKeySetting, PrimaryKeyId);

        builder.UseSetting(
            SigningKeyRing.KeyPrefix + "V1", PrimaryKey);

        builder.UseSetting(
            SigningKeyRing.KeyPrefix + "V2", SecondaryKey);

        if (_apiDocumentation is not null)
        {
            builder.UseSetting(
                HostConfiguration.ApiDocumentationSetting, _apiDocumentation);
        }

        if (_services is not null)
            builder.ConfigureTestServices(_services);
    }
}
