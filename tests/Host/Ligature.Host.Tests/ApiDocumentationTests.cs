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

    /// <summary>
    /// Sign-in succeeds with 204 and no body. A document that still promised a
    /// 200 would send a generated client looking for a carrier in a body that
    /// no longer exists.
    /// </summary>
    [Fact]
    public async Task The_sign_in_operation_documents_204_and_not_200()
    {
        var responses = (await DocumentAsync()).RootElement
            .GetProperty("paths")
            .GetProperty("/api/auth/sign-in")
            .GetProperty("post")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("204", out _), "Sign-in does not document its 204.");
        Assert.False(responses.TryGetProperty("200", out _), "Sign-in still documents a 200.");
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

        // Sign-in no longer returns the carrier in a body, so a bearer caller
        // must be told where to take it from.
        Assert.Contains("Set-Cookie", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// The browser transport is documented as a scheme of its own: an API key
    /// carried in the __Host-ligature cookie. Without it the document would
    /// describe only the transport browsers do not use.
    /// </summary>
    [Fact]
    public async Task The_document_declares_the_carrier_cookie_scheme()
    {
        var document = await DocumentAsync();

        var scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("carrierCookie");

        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("cookie", scheme.GetProperty("in").GetString());
        Assert.Equal("__Host-ligature", scheme.GetProperty("name").GetString());

        Assert.False(
            string.IsNullOrWhiteSpace(scheme.GetProperty("description").GetString()));
    }

    /// <summary>
    /// The authenticated operations accept either transport; the anonymous ones
    /// declare nothing. That split is the documented half of section 17's rule
    /// that an absent credential is not a failure — a reader can see which
    /// operations still run without one.
    ///
    /// Either transport means TWO requirement objects. OpenAPI reads the array
    /// as alternatives and the members of one object as all required together,
    /// so a single object naming both schemes would document a request that
    /// must present the bearer header AND the cookie — which is not the API.
    /// </summary>
    [Fact]
    public async Task Only_the_authenticated_operations_require_a_carrier_by_either_scheme()
    {
        var paths = (await DocumentAsync()).RootElement.GetProperty("paths");

        foreach (var route in new[]
                 {
                     "/api/auth/sign-out",
                     "/api/users",
                     "/api/account/change-password",
                     "/api/account/sign-out-everywhere",
                     "/api/users/{userId}/activation-link",
                     "/api/users/{userId}/role-assignments",
                     "/api/role-assignments/{assignmentId}/revoke",
                     "/api/users/{userId}/deactivate",
                     "/api/users/{userId}/reactivate",
                 })
        {
            var requirements = paths.GetProperty(route).GetProperty("post")
                .GetProperty("security")
                .EnumerateArray()
                .ToList();

            Assert.Equal(2, requirements.Count);

            var named = new List<string>();

            foreach (var requirement in requirements)
            {
                // REGRESSION GUARD. A requirement built without the host
                // document serialises as an empty object: present, but naming
                // no scheme. That is worse than omitting it — a reader sees
                // "secured" and cannot tell by what, and Scalar cannot link it
                // to the token field. Exactly one scheme per object, so neither
                // an empty object nor a combined one passes.
                var scheme = Assert.Single(requirement.EnumerateObject());

                named.Add(scheme.Name);

                // Scopes are meaningless for a carrier that carries no claims;
                // the array must be empty rather than invented.
                Assert.Empty(scheme.Value.EnumerateArray());
            }

            Assert.Equal(
                ["bearer", "carrierCookie"],
                named.Order(StringComparer.Ordinal));
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
