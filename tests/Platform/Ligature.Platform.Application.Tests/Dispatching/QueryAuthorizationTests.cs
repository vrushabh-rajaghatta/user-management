using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Dispatching;
using Ligature.Platform.Application.Users.Queries.Me;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Tests.Dispatching;

/// <summary>
/// The query authorization contract (docs/architecture.md §11).
///
/// WHAT IS BEING DESIGNED AGAINST IS OMISSION, NOT REFUSAL. An unregistered
/// handler fails loudly on first dispatch; a handler that forgets its
/// authorization check fails silently — it serves the data. These tests prove
/// the guardrail that makes forgetting impossible, and deliberately prove
/// nothing about any particular query's authorization outcome.
///
/// The queries below are throwaway. No UsersQuery, no DTO, no database, no
/// pagination, no privacy decision and no audit: those are separate decisions
/// and contaminating the mechanism's tests with them would make this file fail
/// for reasons that have nothing to do with the mechanism.
/// </summary>
public sealed class QueryAuthorizationTests
{
    // ------------------------------------- 1. registration requires a class

    /// <summary>
    /// The compile-time half, asserted structurally because it cannot be
    /// asserted any other way: a test cannot demonstrate a compilation failure
    /// at runtime. So the API SURFACE is what gets pinned — there must be no
    /// registration path that omits the classification.
    ///
    /// Weakening AddQuery to make a negative test executable would be the
    /// opposite of the point.
    /// </summary>
    [Fact]
    public void Registering_a_query_requires_its_type_to_declare_a_classification()
    {
        var methods = typeof(QueryRegistration)
            .GetMethods()
            .Where(x => x.Name == "AddQuery")
            .ToArray();

        var method = Assert.Single(methods);

        var queryParameter = method.GetGenericArguments()[0];

        Assert.Contains(
            typeof(IQueryAuthorizationDeclaration),
            queryParameter.GetGenericParameterConstraints());
    }

    /// <summary>
    /// The same property stated as an absence, because an overload is how the
    /// constraint would most plausibly be escaped: someone adds a convenience
    /// registration "just for an internal query" and the guarantee is gone for
    /// everything.
    /// </summary>
    [Fact]
    public void There_is_no_unclassified_registration_path()
    {
        var unconstrained = typeof(QueryRegistration)
            .GetMethods()
            .Where(x => x.Name == "AddQuery")
            .Where(x => !x.GetGenericArguments()[0]
                .GetGenericParameterConstraints()
                .Contains(typeof(IQueryAuthorizationDeclaration)))
            .ToArray();

        Assert.Empty(unconstrained);
    }

    /// <summary>Both classifications are accepted, and they are the only two.</summary>
    [Fact]
    public void Both_classifications_register()
    {
        var services = new ServiceCollection()
            .AddQuery<OpenQuery, string, OpenHandler>()
            .AddQuery<GatedQuery, string, GatedHandler>();

        Assert.NotNull(services.SingleOrDefault(x =>
            x.ServiceType == typeof(IQueryHandler<OpenQuery, string>)));

        Assert.NotNull(services.SingleOrDefault(x =>
            x.ServiceType == typeof(IQueryHandler<GatedQuery, string>)));
    }

    /// <summary>
    /// A blank permission is not a declaration of anything. Refused where it is
    /// written rather than at start-up, so the mistake is found by the person
    /// making it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_permission_is_not_a_classification(string code)
    {
        Assert.ThrowsAny<ArgumentException>(() => QueryAuthorization.Required(code));
    }

    /// <summary>
    /// Reading the permission of an open query is a programming error, not a
    /// null. The two states must stay distinguishable at every point they are
    /// consumed, or the nullable shape the contract forbids returns by the back
    /// door.
    /// </summary>
    [Fact]
    public void An_open_classification_has_no_permission_to_read()
    {
        Assert.False(QueryAuthorization.NotRequired.IsRequired);

        Assert.ThrowsAny<InvalidOperationException>(
            () => QueryAuthorization.NotRequired.PermissionCode);
    }

    /// <summary>
    /// NULL IS THE THIRD STATE THE CONTRACT RULES OUT, and the one the type
    /// system cannot. The constraint guarantees a declaration exists; a static
    /// member of a reference type can still return null. Refused at both
    /// layers, by the same rule.
    /// </summary>
    [Fact]
    public void A_null_classification_is_refused_at_registration()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddQuery<NullQuery, string, NullHandler>());

        Assert.Contains(nameof(NullQuery), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_classification_registered_outside_the_mechanism_prevents_start_up()
    {
        var services = new ServiceCollection();

        services.AddScoped<IQueryHandler<NullQuery, string>, NullHandler>();

        var failure = Assert.ThrowsAny<Exception>(() => QueryAuthorizationVerification.Verify(services));

        Assert.Contains(nameof(NullQuery), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// PermissionCode throws for NotRequired, deliberately — so a
    /// compiler-generated ToString, which prints every public property, would
    /// throw too: in a debugger, a log line, an assertion message.
    /// </summary>
    [Fact]
    public void A_classification_can_be_printed_whichever_state_it_is_in()
    {
        Assert.Contains("NotRequired", QueryAuthorization.NotRequired.ToString(), StringComparison.Ordinal);
        Assert.Contains("test.read", QueryAuthorization.Required("test.read").ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------- 2. the bypass fails start-up

    /// <summary>
    /// THE ADVERSARIAL ONE. AddQuery cannot be bypassed by accident, only
    /// deliberately — so this does it deliberately, exactly as someone would
    /// who found the constraint inconvenient.
    ///
    /// It asserts more than "something threw". A verification failure that does
    /// not name the offending query leaves an operator with a refused start-up
    /// and nowhere to look, which is a worse outcome than the one it replaced.
    /// </summary>
    [Fact]
    public void A_handler_registered_outside_the_mechanism_prevents_start_up()
    {
        var services = new ServiceCollection();

        services.AddScoped<IQueryHandler<UnclassifiedQuery, string>, UnclassifiedHandler>();

        var failure = Assert.ThrowsAny<Exception>(() => QueryAuthorizationVerification.Verify(services));

        Assert.Contains(nameof(UnclassifiedQuery), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the other side of it: a properly registered application verifies. A
    /// check that refused everything would satisfy the test above and stop the
    /// system from ever starting.
    /// </summary>
    [Fact]
    public void A_properly_registered_application_verifies()
    {
        var services = new ServiceCollection()
            .AddQuery<OpenQuery, string, OpenHandler>()
            .AddQuery<GatedQuery, string, GatedHandler>();

        QueryAuthorizationVerification.Verify(services);
    }

    /// <summary>The real registration list, which must satisfy its own contract.</summary>
    [Fact]
    public void The_application_registers_only_classified_queries()
    {
        QueryAuthorizationVerification.Verify(
            new ServiceCollection().AddPlatformApplication());
    }

    // ------------------------------------------------ 3. /me joins the contract

    /// <summary>
    /// The retroactive consequence, and the point of it: there is no such thing
    /// as an accidentally unauthorised query. /me is self-scoped and refuses
    /// nobody, and it now SAYS SO rather than being silent about it.
    ///
    /// Asserted reflectively so this test needs no change to MeQuery to run —
    /// it fails because the declaration is absent, which is the thing being
    /// proved.
    /// </summary>
    [Fact]
    public void MeQuery_declares_that_it_requires_no_authorization()
    {
        Assert.Contains(
            typeof(IQueryAuthorizationDeclaration),
            typeof(MeQuery).GetInterfaces());

        var declared = (QueryAuthorization)typeof(MeQuery)
            .GetProperty(nameof(IQueryAuthorizationDeclaration.Authorization))!
            .GetValue(null)!;

        Assert.False(declared.IsRequired);
    }

    // --------------------------------------------------------------- fixtures

    private sealed record OpenQuery : IQuery<string>, IQueryAuthorizationDeclaration
    {
        public static QueryAuthorization Authorization => QueryAuthorization.NotRequired;
    }

    private sealed record GatedQuery : IQuery<string>, IQueryAuthorizationDeclaration
    {
        public static QueryAuthorization Authorization => QueryAuthorization.Required("test.read");
    }

    /// <summary>Declares, and declares null. It exists to be rejected.</summary>
    private sealed record NullQuery : IQuery<string>, IQueryAuthorizationDeclaration
    {
        public static QueryAuthorization Authorization => null!;
    }

    private sealed class NullHandler : IQueryHandler<NullQuery, string>
    {
        public Task<string> Handle(NullQuery query, CancellationToken cancellationToken)
            => Task.FromResult("null");
    }

    /// <summary>Deliberately declares nothing. It exists to be rejected.</summary>
    private sealed record UnclassifiedQuery : IQuery<string>;

    private sealed class OpenHandler : IQueryHandler<OpenQuery, string>
    {
        public Task<string> Handle(OpenQuery query, CancellationToken cancellationToken)
            => Task.FromResult("open");
    }

    private sealed class GatedHandler : IQueryHandler<GatedQuery, string>
    {
        public Task<string> Handle(GatedQuery query, CancellationToken cancellationToken)
            => Task.FromResult("gated");
    }

    private sealed class UnclassifiedHandler : IQueryHandler<UnclassifiedQuery, string>
    {
        public Task<string> Handle(UnclassifiedQuery query, CancellationToken cancellationToken)
            => Task.FromResult("unclassified");
    }
}
