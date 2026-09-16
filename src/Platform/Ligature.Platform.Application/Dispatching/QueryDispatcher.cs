using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Dispatching;

/// <summary>
/// Resolves a query's handler and invokes it. That is the whole of it.
///
/// A DISPATCHER IS NOT A PIPELINE. There is deliberately no QueryPipeline
/// beside CommandPipeline, no behaviour collection resolved here, and nothing
/// copied onto queries by symmetry with commands (docs/architecture.md
/// section 11). Each of the four command concerns was decided against
/// explicitly rather than omitted:
///
///   Authentication  the caller is established by middleware BEFORE dispatch,
///                   so a query needs no machinery of its own to produce one.
///   Authorization   no generic contract on queries yet. The query that first
///                   requires one introduces it.
///   Transaction     queries do not inherit the command transaction boundary.
///   Audit           the audit boundary covers state-changing operations.
///
/// QueryDispatcherTests holds this line structurally: query dispatch is proved
/// to work in a container where none of the command behaviour infrastructure
/// is registered at all.
/// </summary>
public sealed class QueryDispatcher : IQueryDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public QueryDispatcher(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// GetRequiredService, so a handler that exists but was never registered
    /// fails loudly on first dispatch. Registration is explicit and nothing
    /// scans, which is what makes "is this query wired in?" answerable.
    /// </summary>
    public Task<TResult> SendAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        var handler =
            _serviceProvider
                .GetRequiredService<IQueryHandler<TQuery, TResult>>();

        return handler.Handle(query, cancellationToken);
    }
}
