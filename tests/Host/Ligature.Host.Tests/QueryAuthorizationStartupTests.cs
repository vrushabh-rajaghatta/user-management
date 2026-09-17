using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Host.Tests;

/// <summary>
/// The ENFORCEMENT POINT is connected, not merely implemented
/// (docs/architecture.md section 11).
///
/// QueryAuthorizationTests proves the verifier refuses an unclassified query.
/// That says nothing about whether the host calls it: delete the one line in
/// Program.cs and every one of those tests still passes, while the application
/// happily starts with a handler that serves data to anyone. This drives the
/// real start-up composition path instead, so the line cannot be removed
/// without a test dying.
///
/// Same shape as the host's other refused start-ups — an unreadable setting, a
/// missing signing key — because a query registered around the contract belongs
/// in the same category: the process must not come up.
/// </summary>
public sealed class QueryAuthorizationStartupTests
{
    [Fact]
    public void A_query_handler_registered_around_the_contract_refuses_start_up()
    {
        using var factory = new HostFactory(services: services =>
            services.AddScoped<IQueryHandler<UnclassifiedQuery, string>, UnclassifiedHandler>());

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(nameof(UnclassifiedQuery), Unwrap(failure), StringComparison.Ordinal);
    }

    /// <summary>
    /// The counterweight. A host that refused to start whatever it was given
    /// would satisfy the test above and be useless — and the application's own
    /// registrations, /me among them, are what it is checked against here.
    /// </summary>
    [Fact]
    public void The_application_as_registered_starts()
    {
        using var factory = new HostFactory();

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    private static string Unwrap(Exception failure)
    {
        var messages = new List<string>();

        for (var current = failure; current is not null; current = current.InnerException)
            messages.Add(current.Message);

        return string.Join(" | ", messages);
    }

    /// <summary>Declares no classification. It exists to be refused.</summary>
    private sealed record UnclassifiedQuery : IQuery<string>;

    private sealed class UnclassifiedHandler : IQueryHandler<UnclassifiedQuery, string>
    {
        public Task<string> Handle(UnclassifiedQuery query, CancellationToken cancellationToken)
            => Task.FromResult("served to anyone");
    }
}
