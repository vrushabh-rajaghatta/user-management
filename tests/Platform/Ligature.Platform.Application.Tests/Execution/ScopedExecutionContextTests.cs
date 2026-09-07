using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Tests.Execution;

public sealed class ScopedExecutionContextTests
{
    // ------------------------------------------------------------ unset state

    [Fact]
    public void An_unestablished_context_is_not_authenticated()
    {
        Assert.False(new ScopedExecutionContext().IsAuthenticated);
    }

    [Fact]
    public void An_unestablished_context_has_no_UserId()
    {
        var context = new ScopedExecutionContext();

        Assert.Throws<InvalidOperationException>(() => context.UserId);
    }

    /// <summary>
    /// The dangerous one. default(ActorType) is Human, because Human is the
    /// first enum member — so a context that returned a default instead of
    /// throwing would claim to be a human actor and pass HumanActorBehavior,
    /// the check standing between an automated caller and every signature,
    /// approval and role-granting permission in the catalogue.
    /// </summary>
    [Fact]
    public void An_unestablished_context_does_not_silently_claim_to_be_human()
    {
        var context = new ScopedExecutionContext();

        Assert.Equal(ActorType.Human, default(ActorType));

        Assert.Throws<InvalidOperationException>(() => context.ActorType);
    }

    // ----------------------------------------------------------- establishing

    [Fact]
    public void Establishing_sets_authentication_id_and_actor_type_together()
    {
        var context = new ScopedExecutionContext();
        var userId = UserId.New();

        context.Establish(userId, ActorType.Human);

        Assert.True(context.IsAuthenticated);
        Assert.Equal(userId, context.UserId);
        Assert.Equal(ActorType.Human, context.ActorType);
    }

    [Fact]
    public void An_agent_may_be_established()
    {
        var context = new ScopedExecutionContext();

        context.Establish(UserId.New(), ActorType.Agent);

        Assert.Equal(ActorType.Agent, context.ActorType);
    }

    /// <summary>
    /// The System actor cannot authenticate, holds no identities and performs
    /// no business actions (SA, UI8, UR7). Writes with no human caller are
    /// attributed to it by the provenance interceptor — which is a different
    /// thing from it being the established caller.
    /// </summary>
    [Fact]
    public void The_System_actor_cannot_be_established_as_the_caller()
    {
        var context = new ScopedExecutionContext();

        var failure = Assert.Throws<DomainException>(
            () => context.Establish(User.SystemUserId, ActorType.System));

        Assert.Contains("cannot authenticate", failure.Message);

        Assert.False(
            context.IsAuthenticated,
            "A rejected Establish must leave the context unset.");
    }

    [Fact]
    public void A_null_user_id_is_rejected()
    {
        var context = new ScopedExecutionContext();

        Assert.Throws<ArgumentNullException>(
            () => context.Establish(null!, ActorType.Human));

        Assert.False(context.IsAuthenticated);
    }

    /// <summary>
    /// A second Establish means a leaked scope or an impersonation attempt.
    /// Either should stop rather than quietly rebind the identity that every
    /// audit record written in this scope will carry.
    /// </summary>
    [Fact]
    public void The_caller_cannot_be_changed_once_established()
    {
        var context = new ScopedExecutionContext();
        var original = UserId.New();

        context.Establish(original, ActorType.Human);

        Assert.Throws<InvalidOperationException>(
            () => context.Establish(UserId.New(), ActorType.Human));

        Assert.Equal(original, context.UserId);
    }

    // ------------------------------------------------------------- read-only

    /// <summary>
    /// A consumer injecting IExecutionContext must not be able to rewrite the
    /// caller. If a mutating member is ever added to the read interface, every
    /// behaviour and handler in the application gains that power silently.
    /// </summary>
    [Fact]
    public void The_read_interface_exposes_no_way_to_mutate_the_caller()
    {
        var properties = typeof(IExecutionContext).GetProperties();

        Assert.Equal(
            new[] { "ActorType", "IsAuthenticated", "UserId" },
            properties
                .Select(x => x.Name)
                .OrderBy(x => x, StringComparer.Ordinal));

        Assert.All(
            properties,
            p => Assert.False(
                p.CanWrite,
                $"IExecutionContext.{p.Name} is settable, so any injected "
                + "consumer could rewrite the calling identity."));

        // Only the three property getters — no Establish, Set, Clear or
        // Impersonate hiding as a method.
        var methods = typeof(IExecutionContext)
            .GetMethods()
            .Where(x => !x.IsSpecialName)
            .Select(x => x.Name)
            .ToList();

        Assert.Empty(methods);
    }

    // -------------------------------------------------------------------- DI

    /// <summary>
    /// Both interfaces must resolve to the SAME scoped instance. Two separate
    /// AddScoped&lt;Interface, Impl&gt;() registrations would give one scope two
    /// contexts, and establishing the caller on one would be invisible to the
    /// other — the pipeline would then reject every authenticated command.
    /// </summary>
    [Fact]
    public void The_read_and_write_sides_share_one_instance_per_scope()
    {
        using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        var read = scope.ServiceProvider.GetRequiredService<IExecutionContext>();
        var write = scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>();

        Assert.Same(read, write);

        var userId = UserId.New();
        write.Establish(userId, ActorType.Human);

        Assert.True(read.IsAuthenticated);
        Assert.Equal(userId, read.UserId);
    }

    [Fact]
    public void Each_scope_gets_its_own_caller()
    {
        using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .BuildServiceProvider();

        using (var first = provider.CreateScope())
        {
            first.ServiceProvider
                .GetRequiredService<IExecutionContextInitializer>()
                .Establish(UserId.New(), ActorType.Human);
        }

        using var second = provider.CreateScope();

        Assert.False(
            second.ServiceProvider
                .GetRequiredService<IExecutionContext>()
                .IsAuthenticated,
            "A caller established in one scope must not leak into the next.");
    }

    /// <summary>
    /// Provisioning runs with no authenticated caller and must keep working:
    /// the interceptor reads the absence and attributes the write to the
    /// System actor.
    /// </summary>
    [Fact]
    public void A_resolved_but_unestablished_context_reports_unauthenticated()
    {
        using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        Assert.False(
            scope.ServiceProvider
                .GetRequiredService<IExecutionContext>()
                .IsAuthenticated);
    }
}
