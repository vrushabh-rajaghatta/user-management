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
/// Declares the bearer scheme in the published document, and marks the
/// operations that need it (docs/architecture.md sections 17 and 18).
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
                    "The access carrier returned by POST /api/auth/sign-in, as "
                    + "'Authorization: Bearer <carrier>'.\n\n"
                    + "It is a signed reference to a server-side session and "
                    + "carries no claims of its own — the session row is the "
                    + "sole authority on whether it is still valid, which is "
                    + "what lets a revocation or a deactivation take effect on "
                    + "the very next request.\n\n"
                    + "It has no independent expiry. An absent header is not an "
                    + "error: anonymous operations still run.",
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

            // The reference is constructed WITH the host document. Without it
            // the requirement serialises as an empty object — present in the
            // document, but naming no scheme, which is worse than absent: a
            // reader sees "this is secured" and cannot tell by what.
            operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = [],
                });

            return Task.CompletedTask;
        });
    }
}
