using Ligature.Host.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ligature.Host.Configuration;

/// <summary>
/// Marks an operation as requiring an access carrier, for the OpenAPI document
/// only.
///
/// It grants and enforces nothing. Authentication is decided by the command
/// pipeline, and an endpoint carrying this marker is no more protected than one
/// without it — which is exactly why the marker exists separately rather than
/// being inferred: nothing here should look like it is doing the enforcing.
/// </summary>
internal sealed class RequiresCarrier;

/// <summary>
/// Declares the carrier's two transports in the published document — the
/// bearer header and the browser cookie — and marks the operations that need a
/// carrier (docs/architecture.md sections 17 and 18).
///
/// Without this the document describes an authenticated API without describing
/// its authentication: a reader — a client generator, or the Scalar reference
/// UI — cannot tell that POST /api/users needs a carrier, or that
/// /api/account/activate deliberately does not. Scalar builds its authentication
/// panel from the declared schemes, so with none declared it has no token field
/// and every request it sends is anonymous.
/// </summary>
internal static class ApiSecurity
{
    internal const string SchemeId = "bearer";

    internal const string CookieSchemeId = "carrierCookie";

    internal static void AddCarrierSecurity(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();

            document.Components.SecuritySchemes ??=
                new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

            document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                Description =
                    "The access carrier, as 'Authorization: Bearer <carrier>'. "
                    + "POST /api/auth/sign-in delivers it only in the "
                    + "__Host-ligature cookie of its Set-Cookie header, never in "
                    + "a response body; a caller that is not a browser takes it "
                    + "from there.\n\n"
                    + "It is a signed reference to a server-side session and "
                    + "carries no claims of its own — the session row is the "
                    + "sole authority on whether it is still valid, which is "
                    + "what lets a revocation or a deactivation take effect on "
                    + "the very next request.\n\n"
                    + "It has no independent expiry. An absent credential is not "
                    + "an error: anonymous operations still run. A request that "
                    + "presents an Authorization header is judged on that header "
                    + "alone, and any carrier cookie sent with it is ignored.",
            };

            document.Components.SecuritySchemes[CookieSchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = CarrierCookie.Name,
                Description =
                    "The same access carrier, as the browser transport: the "
                    + "cookie POST /api/auth/sign-in sets. It is HttpOnly, Secure, "
                    + "SameSite=Strict and scoped to the whole origin, so the "
                    + "page's scripts cannot read it.\n\n"
                    + "Signing out clears it, and so does signing out everywhere "
                    + "unless the current session is kept. A state-changing "
                    + "request from another site is refused before the cookie is "
                    + "read.",
            };

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var requiresCarrier = context.Description.ActionDescriptor
                .EndpointMetadata.OfType<RequiresCarrier>().Any();

            if (!requiresCarrier)
                return Task.CompletedTask;

            operation.Security ??= [];

            // One requirement object PER scheme. OpenAPI reads the array as
            // alternatives and the schemes inside one object as all required
            // together, so a single object naming both would document a request
            // that must present the header AND the cookie — not this API.
            //
            // Each reference is constructed WITH the host document. Without it
            // the requirement serialises as an empty object — present in the
            // document, but naming no scheme, which is worse than absent: a
            // reader sees "this is secured" and cannot tell by what.
            foreach (var schemeId in new[] { SchemeId, CookieSchemeId })
            {
                operation.Security.Add(
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(schemeId, context.Document)] = [],
                    });
            }

            return Task.CompletedTask;
        });
    }
}
