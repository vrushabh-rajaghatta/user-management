using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Dispatching;

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

        if (authorization.IsRequired && string.IsNullOrWhiteSpace(authorization.PermissionCode))
            return $"{queryType.FullName} requires a permission but names none.";

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
    /// Reads the declaration through a generic method, so the READ itself is
    /// the same compile-time-typed TQuery.Authorization that AddQuery uses —
    /// explicit interface implementations included. Only the dispatch to it is
    /// reflective, because a registration descriptor carries a Type and no
    /// generic parameter.
    ///
    /// This is not the reflection the pattern note rules out. That note is
    /// about DECLARING by an attribute or member nobody can be forced to write;
    /// here the declaration is already compiler-enforced on the normal path,
    /// and this only reads it back to catch the bypass.
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
