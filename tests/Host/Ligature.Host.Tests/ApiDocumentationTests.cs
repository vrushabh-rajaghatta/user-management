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
    private static string Unwrap(Exception failure)
    {
        var messages = new List<string>();

        for (var current = failure; current is not null; current = current.InnerException)
            messages.Add(current.Message);

        return string.Join(" | ", messages);
    }
}
