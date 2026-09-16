using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Dispatching;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Tests.Dispatching;

/// <summary>
/// The query seam decided in B6-A (docs/architecture.md §11):
///
///     HTTP endpoint -> IQueryDispatcher -> IQueryHandler -> read
///
/// A DISPATCHER IS NOT A PIPELINE. There is deliberately no query pipeline and
/// no query behaviour, and nothing is copied onto queries by symmetry with
/// commands. Two tests hold that line rather than a statement in a document:
/// one proves dispatch needs none of the command infrastructure, and the other
/// guards the dispatcher's dependency surface, which is where a pipeline would
/// have to arrive.
/// </summary>
public sealed class QueryDispatcherTests
{
    [Fact]
    public async Task SendAsync_should_resolve_and_invoke_the_registered_handler()
    {
        var provider = Minimal().BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<IQueryDispatcher>();

        var result = await dispatcher.SendAsync<TestQuery, string>(
            new TestQuery("Ada"),
            CancellationToken.None);

        Assert.Equal("Answered Ada", result);
    }

    /// <summary>
    /// Handlers are registered explicitly, one line each, so "is this query
    /// wired in?" has an answer. A missing registration must be a loud failure
    /// on first dispatch rather than a silently absent result.
    /// </summary>
    [Fact]
    public async Task SendAsync_should_fail_loudly_when_no_handler_is_registered()
    {
        var provider = new ServiceCollection()
            .AddScoped<IQueryDispatcher, QueryDispatcher>()
            .BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<IQueryDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync<TestQuery, string>(
                new TestQuery("Ada"),
                CancellationToken.None));
    }

    /// <summary>
    /// THE INVARIANT THAT MATTERS.
    ///
    /// Nothing is registered here but the dispatcher and one handler — no
    /// execution context, no audit collector, no transaction scope, no clock,
    /// none of the infrastructure every command behaviour depends on. A query
    /// dispatched in that container must still succeed.
    ///
    /// This proves something stronger than asserting that a behaviour list is
    /// empty: it demonstrates that query dispatch does not depend on the
    /// command behaviour infrastructure at all. If a query pipeline is
    /// introduced later and a behaviour quietly requires any of those
    /// services, this test fails rather than the decision eroding unnoticed.
    /// </summary>
    [Fact]
    public async Task Query_dispatch_should_not_depend_on_command_behaviour_infrastructure()
    {
        var provider = Minimal().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        using var scope = provider.CreateScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

        var result = await dispatcher.SendAsync<TestQuery, string>(
            new TestQuery("Grace"),
            CancellationToken.None);

        Assert.Equal("Answered Grace", result);
    }

    /// <summary>
    /// THE STRUCTURAL INVARIANT, and the stronger of the two.
    ///
    /// The decision is not "the dispatcher happens to work without command
    /// infrastructure" — it is that THERE IS NO QUERY PIPELINE. The test below
    /// proves the first; this one guards the second, because anyone
    /// introducing a pipeline would inject it here.
    ///
    /// The minimal-container test alone is not enough: resolving an EMPTY
    /// behaviour collection never throws, so a pipeline that resolved
    /// behaviours would sail through it unnoticed.
    /// </summary>
    [Fact]
    public void The_query_dispatcher_depends_on_nothing_but_the_service_provider()
    {
        var constructor = Assert.Single(typeof(QueryDispatcher).GetConstructors());

        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(IServiceProvider), parameter.ParameterType);
    }

    [Fact]
    public void The_query_dispatcher_is_registered_by_the_application()
    {
        var descriptor = new ServiceCollection()
            .AddPlatformApplication()
            .SingleOrDefault(x => x.ServiceType == typeof(IQueryDispatcher));

        Assert.NotNull(descriptor);

        Assert.Equal(typeof(QueryDispatcher), descriptor!.ImplementationType);

        // Scoped, as the command dispatcher is: a query belongs to the request
        // that asked it.
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>
    /// The dispatcher and a single handler. Deliberately NOT
    /// AddPlatformApplication(), which would bring the command infrastructure
    /// with it and make the third test prove nothing.
    /// </summary>
    private static ServiceCollection Minimal()
    {
        var services = new ServiceCollection();

        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        services.AddScoped<IQueryHandler<TestQuery, string>, TestHandler>();

        return services;
    }

    private sealed record TestQuery(string Name) : IQuery<string>;

    private sealed class TestHandler : IQueryHandler<TestQuery, string>
    {
        public Task<string> Handle(TestQuery query, CancellationToken cancellationToken)
            => Task.FromResult($"Answered {query.Name}");
    }
}
