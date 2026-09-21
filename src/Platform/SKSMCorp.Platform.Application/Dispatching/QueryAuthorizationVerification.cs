using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace SKSMCorp.Platform.Application.Dispatching;

/// <summary>
/// "Does every registered query handler have an authorization classification?"
///
/// The second of the two layers §11 requires. The first is the compile-time
/// constraint on AddQuery; this one catches a handler registered outside that
/// mechanism, by a raw AddScoped.
///
/// It inspects REGISTRATIONS THE APPLICATION EXPLICITLY MADE. It does not
/// discover handlers by inspecting assemblies, and must never be changed to:
/// the question is "what did this application register, and did it satisfy the
/// contract?", not "what query handlers exist?".
/// </summary>
public static class QueryAuthorizationVerification
{
    /// <summary>
    /// Throws with every violation named, and the host lets that stop the
    /// process — as AuditDeclarations.VerifyAgainst already does.
    /// </summary>
    public static void Verify(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var violations = services
            .Where(IsQueryHandlerRegistration)
            .Select(ViolationIn)
            .OfType<string>()
            .ToList();

        if (violations.Count == 0)
            return;

        throw new InvalidOperationException(
            "Query authorization verification failed. Every registered query "
            + "handler must be registered with AddQuery, and its query must "
            + "declare NotRequired or Required(permission) (docs/architecture.md "
            + "section 11):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(x => $"  - {x}")));
    }

    /// <summary>
    /// The single definition of a valid classification, shared with AddQuery.
    /// Null for a valid one; otherwise what is wrong with it.
    /// </summary>
    internal static string? ProblemWith(Type queryType, QueryAuthorization? authorization)
    {
        if (authorization is null)
            return $"{queryType.FullName} declares a null authorization classification.";

        // The second layer. The factories already refuse both of these, so a
        // classification reaching here in either state was built by some path
        // that bypassed them — which is precisely what start-up verification
        // exists to catch.
        if (authorization.IsRequired && authorization.PermissionCodes.Count == 0)
            return $"{queryType.FullName} requires a permission but names none.";

        if (authorization.IsRequired && authorization.PermissionCodes.Any(string.IsNullOrWhiteSpace))
            return $"{queryType.FullName} requires a permission but names a blank one.";

        return null;
    }

    private static bool IsQueryHandlerRegistration(ServiceDescriptor descriptor)
        => descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>);

    private static string? ViolationIn(ServiceDescriptor descriptor)
    {
        var handler = (descriptor.IsKeyedService
                ? descriptor.KeyedImplementationType
                : descriptor.ImplementationType)?.FullName
            ?? "a factory or instance";

        // An open generic registration handles queries nobody can name here,
        // so it cannot be verified — and a registration that cannot be verified
        // is exactly what this exists to refuse.
        if (descriptor.ServiceType.IsGenericTypeDefinition)
            return $"an open generic query handler ({handler}) cannot be verified.";

        var queryType = descriptor.ServiceType.GetGenericArguments()[0];

        if (!typeof(IQueryAuthorizationDeclaration).IsAssignableFrom(queryType))
            return $"{queryType.FullName}, handled by {handler}, declares no authorization classification.";

        return ProblemWith(queryType, Read(queryType));
    }

    /// <summary>
    /// THE RULE THIS METHOD EXISTS TO KEEP, stated for whoever is tempted to
    /// simplify it: reflection may discover WHICH QUERY TYPE was registered.
    /// It must never READ THE DECLARATION.
    ///
    ///     allowed    descriptor -> closed IQueryHandler<TQuery,TResult>
    ///                -> TQuery -> MakeGenericMethod -> TQuery.Authorization
    ///
    ///     forbidden  reflection -> find an "Authorization" property -> invoke it
    ///
    /// The first uses reflection only as a bridge from runtime registration
    /// metadata, which carries a Type and no generic parameter, to a statically
    /// typed method; the declaration is then read through the compile-time
    /// interface contract, exactly as AddQuery reads it, explicit interface
    /// implementations included.
    ///
    /// The second would undo the reason static abstract members were chosen
    /// (docs/architecture.md section 11, pattern note). It looks like the same
    /// thing in fewer lines, and it is not: a property found by name is a
    /// convention, and a convention is precisely what the contract replaced.
    /// </summary>
    private static QueryAuthorization? Read(Type queryType)
        => (QueryAuthorization?)typeof(QueryAuthorizationVerification)
            .GetMethod(nameof(ReadDeclaration), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(queryType)
            .Invoke(null, null);

    private static QueryAuthorization? ReadDeclaration<TQuery>()
        where TQuery : IQueryAuthorizationDeclaration
        => TQuery.Authorization;
}
