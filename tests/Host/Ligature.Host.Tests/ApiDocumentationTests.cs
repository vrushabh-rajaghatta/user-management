using System.Net;
using System.Text.Json;
using Ligature.Host.Configuration;

namespace Ligature.Host.Tests;

/// <summary>
/// The opt-in documentation surface (docs/architecture.md section 18).
///
/// PURE, in the sense AGENTS.md section 3 uses for AccessCarrierTests and
/// SigningKeyRingTests: the host is built and routed, but neither the OpenAPI
/// document nor the Scalar UI touches the database, so these assert something
/// real whether or not PostgreSQL is up.
///
/// The first test is the one that matters. Publishing the shape of an
/// authentication API is a decision, and the evidence worth having is that
/// doing nothing does not make it.
/// </summary>
public sealed class ApiDocumentationTests
{
    private const string DocumentRoute = "/openapi/v1.json";

    private const string ReferenceRoute = "/scalar/";

    // ------------------------------------------------------------- absent

    [Fact]
    public async Task Neither_route_exists_when_the_setting_is_absent()
    {
        // The default factory sets nothing, which is exactly how a deployment
        // that never heard of this feature is configured.
        using var factory = new HostFactory();
        using var client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(DocumentRoute)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(ReferenceRoute)).StatusCode);
    }

    [Fact]
    public async Task Neither_route_exists_when_the_setting_is_false()
    {
        using var factory = new HostFactory("false");
        using var client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(DocumentRoute)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(ReferenceRoute)).StatusCode);
    }

    /// <summary>
    /// The API itself must not depend on the documentation being off. A gate
    /// that accidentally unmapped the endpoints would still pass both tests
    /// above.
    /// </summary>
    [Fact]
    public async Task The_api_is_still_routed_when_documentation_is_absent()
    {
        using var factory = new HostFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/auth/sign-in",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        // The empty body fails binding validation, which is a 400. Any status
        // other than 404 proves the route is mapped; asserting the real one
        // keeps this honest.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------ enabled

    [Fact]
    public async Task The_document_describes_every_endpoint_when_enabled()
    {
        using var factory = new HostFactory("true");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(DocumentRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var paths = document.RootElement.GetProperty("paths");

        // Every route the host maps. A new endpoint that never reaches the
        // document is a documentation gap, and this is what notices.
        Assert.True(paths.TryGetProperty("/api/auth/sign-in", out _));
        Assert.True(paths.TryGetProperty("/api/auth/sign-out", out _));
        Assert.True(paths.TryGetProperty("/api/account/activate", out _));
    }

    /// <summary>
    /// The summaries are the reason the document is worth serving at all, so
    /// their presence is asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task The_document_carries_the_endpoint_summaries()
    {
        using var factory = new HostFactory("true");
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(
            await client.GetStringAsync(DocumentRoute));

        var signIn = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/auth/sign-in")
            .GetProperty("post");

        Assert.False(
            string.IsNullOrWhiteSpace(signIn.GetProperty("summary").GetString()));

        Assert.False(
            string.IsNullOrWhiteSpace(
                signIn.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task The_scalar_reference_is_served_when_enabled()
    {
        using var factory = new HostFactory("true");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ReferenceRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains(
            "Scalar",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ refusal

    /// <summary>
    /// A typo must not read as "off". The host already refuses to start on a
    /// missing connection string and an undersized signing key; a value it
    /// cannot interpret belongs in the same category.
    /// </summary>
    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("enabled")]
    public void A_value_that_is_not_a_boolean_refuses_to_start(string value)
    {
        using var factory = new HostFactory(value);

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            HostConfiguration.ApiDocumentationSetting,
            Unwrap(failure),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The host builder may present the startup failure wrapped, and which
    /// wrapper is used is not part of the contract. The message is.
    /// </summary>
    /// <summary>
    /// The document must describe the API's authentication, not just its
    /// routes. Without a declared scheme a reader — a client generator, or the
    /// Scalar reference UI — has no way to know that a carrier is expected, and
    /// Scalar has no field to put one in, so every request it sends is
    /// anonymous and every authenticated endpoint answers 401.
    /// </summary>
    [Fact]
    public async Task The_document_declares_the_bearer_scheme()
    {
        var document = await DocumentAsync();

        var scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());

        // The description carries the two facts a caller cannot infer from the
        // scheme type: the carrier has no independent expiry, and an absent
        // header is not an error.
        var description = scheme.GetProperty("description").GetString() ?? "";

        Assert.Contains("no independent expiry", description, StringComparison.Ordinal);
        Assert.Contains("not an error", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// The authenticated operations require the scheme; the anonymous ones
    /// declare nothing. That split is the documented half of section 17's rule
    /// that an absent header is not a failure — a reader can see which
    /// operations still run without one.
    /// </summary>
    [Fact]
    public async Task Only_the_authenticated_operations_require_the_scheme()
    {
        var paths = (await DocumentAsync()).RootElement.GetProperty("paths");

        foreach (var route in new[] { "/api/auth/sign-out", "/api/users" })
        {
            var requirements = paths.GetProperty(route).GetProperty("post")
                .GetProperty("security");

            var requirement = Assert.Single(requirements.EnumerateArray());

            // REGRESSION GUARD. A requirement built without the host document
            // serialises as an empty object: present, but naming no scheme.
            // That is worse than omitting it — a reader sees "secured" and
            // cannot tell by what, and Scalar cannot link it to the token
            // field. Asserting the array is non-empty would not have caught it.
            Assert.True(
                requirement.TryGetProperty("bearer", out var scopes),
                $"{route} declares a security requirement that names no scheme.");

            // Scopes are meaningless for a bearer carrier that carries no
            // claims; the array must be empty rather than invented.
            Assert.Empty(scopes.EnumerateArray());
        }

        foreach (var route in new[] { "/api/auth/sign-in", "/api/account/activate" })
        {
            Assert.False(
                paths.GetProperty(route).GetProperty("post")
                    .TryGetProperty("security", out _),
                $"{route} is anonymous and must declare no security requirement.");
        }
    }

    private static async Task<JsonDocument> DocumentAsync()
    {
        await using var factory = new HostFactory("true");

        var response = await factory.CreateClient().GetAsync(DocumentRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string Unwrap(Exception failure)
    {
        var messages = new List<string>();

        for (var current = failure; current is not null; current = current.InnerException)
            messages.Add(current.Message);

        return string.Join(" | ", messages);
    }
}
