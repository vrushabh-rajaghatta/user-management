using SKSMCorp.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// The approved way to register a query handler (docs/architecture.md §11).
///
/// THERE IS NO UNCLASSIFIED OVERLOAD, and that is the whole point. The
/// constraint on TQuery makes a query without an authorization classification
/// a COMPILE ERROR rather than a start-up failure — the ordinary omission never
/// reaches a running system.
///
/// Start-up verification exists for the other path: someone who bypasses this
/// helper and calls AddScoped directly. The two layers catch different
/// failures, and neither is a behaviour.
/// </summary>
public static class QueryRegistration
{
    public static IServiceCollection AddQuery<TQuery, TResult, THandler>(
        this IServiceCollection services)
        where TQuery : IQuery<TResult>, IQueryAuthorizationDeclaration
        where THandler : class, IQueryHandler<TQuery, TResult>
    {
        ArgumentNullException.ThrowIfNull(services);

        // The constraint guarantees a declaration EXISTS; it cannot guarantee
        // one is USABLE. A static member of a reference type can still return
        // null, so the value is checked here too, by the same rule start-up
        // verification applies — one definition of "valid", not two.
        var problem = Dispatching.QueryAuthorizationVerification.ProblemWith(
            typeof(TQuery), TQuery.Authorization);

        if (problem is not null)
            throw new InvalidOperationException(problem);

        services.AddScoped<IQueryHandler<TQuery, TResult>, THandler>();

        return services;
    }
}
