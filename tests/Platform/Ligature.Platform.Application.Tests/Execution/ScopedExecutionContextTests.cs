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

        context.Establish(userId, ActorType.Human, TestActorIdentity.Human());

        Assert.True(context.IsAuthenticated);
        Assert.Equal(userId, context.UserId);
        Assert.Equal(ActorType.Human, context.ActorType);
    }

    [Fact]
    public void An_agent_may_be_established()
    {
        var context = new ScopedExecutionContext();

        context.Establish(UserId.New(), ActorType.Agent, TestActorIdentity.NonHuman());

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
            () => context.Establish(User.SystemUserId, ActorType.System, TestActorIdentity.NonHuman()));

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
            () => context.Establish(null!, ActorType.Human, TestActorIdentity.Human()));

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

        context.Establish(original, ActorType.Human, TestActorIdentity.Human());

        Assert.Throws<InvalidOperationException>(
            () => context.Establish(
                UserId.New(), ActorType.Human, TestActorIdentity.Human()));

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

        // Identity and Authority were added for the actor snapshot. Both are
        // reads, so the property this test guards is unchanged — but the list
        // is deliberately exhaustive rather than a subset, so that adding a
        // member has to be a decision someone makes here.
        Assert.Equal(
            new[]
            {
                "ActorType", "Authority", "Identity", "IsAuthenticated", "UserId",
            },
            properties
                .Select(x => x.Name)
                .OrderBy(x => x, StringComparer.Ordinal));

        Assert.All(
            properties,
            p => Assert.False(
                p.CanWrite,
                $"IExecutionContext.{p.Name} is settable, so any injected "
                + "consumer could rewrite the calling identity."));

        // Only property getters — no Establish, Set, Clear or
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
        write.Establish(userId, ActorType.Human, TestActorIdentity.Human());

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
                .Establish(UserId.New(), ActorType.Human, TestActorIdentity.Human());
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

    // --------------------------------------------------------- authority

    private static AuthorizingAssignment SomeAuthority()
        => new(
            RoleId.New(),
            "Reviewer",
            ScopeType.Global,
            null,
            UserRoleId.New());

    [Fact]
    public void Authority_is_absent_until_a_command_is_authorised()
    {
        var context = new ScopedExecutionContext();

        context.Establish(
            UserId.New(), ActorType.Human, TestActorIdentity.Human());

        // Not an error state. Sign-in, self-service and token-bearer commands
        // are authenticated and authorised by no role at all (AUD-D28).
        Assert.Null(context.Authority);
    }

    [Fact]
    public void Authority_is_visible_while_its_command_runs()
    {
        var context = new ScopedExecutionContext();
        var authority = SomeAuthority();

        context.Establish(
            UserId.New(), ActorType.Human, TestActorIdentity.Human());

        using (context.EstablishAuthority(authority))
        {
            Assert.Same(authority, context.Authority);
        }
    }

    /// <summary>
    /// A DI scope may dispatch several commands, so authority must not outlive
    /// the one that established it. If it did, the next command would be
    /// recorded under the previous command's authorising assignment — a false
    /// audit record, which is worse than none.
    /// </summary>
    [Fact]
    public void Authority_does_not_outlive_the_command_that_established_it()
    {
        var context = new ScopedExecutionContext();

        context.Establish(
            UserId.New(), ActorType.Human, TestActorIdentity.Human());

        using (context.EstablishAuthority(SomeAuthority()))
        {
        }

        Assert.Null(context.Authority);
    }

    [Fact]
    public void A_later_command_in_the_same_scope_establishes_its_own_authority()
    {
        var context = new ScopedExecutionContext();

        context.Establish(
            UserId.New(), ActorType.Human, TestActorIdentity.Human());

        var first = SomeAuthority();
        var second = SomeAuthority();

        using (context.EstablishAuthority(first))
        {
            Assert.Same(first, context.Authority);
        }

        using (context.EstablishAuthority(second))
        {
            Assert.Same(second, context.Authority);
        }
    }

    /// <summary>
    /// Sequential commands are ordinary; a NESTED establishment is a defect,
    /// because whichever answer were kept would attribute one command's act to
    /// the other's authority.
    /// </summary>
    [Fact]
    public void Authority_cannot_be_nested()
    {
        var context = new ScopedExecutionContext();

        context.Establish(
            UserId.New(), ActorType.Human, TestActorIdentity.Human());

        using var outer = context.EstablishAuthority(SomeAuthority());

        Assert.Throws<InvalidOperationException>(
            () => context.EstablishAuthority(SomeAuthority()));
    }

    [Fact]
    public void Authority_cannot_be_established_without_a_caller()
    {
        var context = new ScopedExecutionContext();

        Assert.Throws<InvalidOperationException>(
            () => context.EstablishAuthority(SomeAuthority()));
    }

    // ---------------------------------------------------------- identity

    [Fact]
    public void The_identity_snapshot_is_returned_as_captured()
    {
        var context = new ScopedExecutionContext();
        var identity = TestActorIdentity.Human("Ada Lovelace");

        context.Establish(UserId.New(), ActorType.Human, identity);

        Assert.Same(identity, context.Identity);
    }

    [Fact]
    public void An_unestablished_context_has_no_identity()
    {
        var context = new ScopedExecutionContext();

        Assert.Throws<InvalidOperationException>(() => context.Identity);
    }

    /// <summary>
    /// AR11 makes this a database CHECK on the audit record. Refusing it here
    /// means the contradiction surfaces where the value was built, rather than
    /// as a constraint violation on a later audit write.
    /// </summary>
    [Fact]
    public void A_non_human_actor_cannot_carry_an_email()
    {
        var context = new ScopedExecutionContext();

        Assert.Throws<DomainException>(
            () => context.Establish(
                UserId.New(),
                ActorType.Agent,
                TestActorIdentity.Human()));
    }
}
