using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Behaviors;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Tests.Behaviors;

/// <summary>
/// CRD-C1's anonymous-command model. The property that matters is the
/// DIRECTION of the default: authentication is required unless a command opts
/// out, so forgetting the marker rejects a command that should have run —
/// loudly — rather than exposing one that should not have.
/// </summary>
public sealed class AnonymousCommandTests
{
    [Fact]
    public async Task An_authenticated_command_still_requires_a_caller()
    {
        var behavior = Behavior<AuthenticatedCommand>(new ScopedExecutionContext());

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => behavior.Handle(
                new AuthenticatedCommand(), CancellationToken.None, Reached));
    }

    [Fact]
    public async Task An_anonymous_command_runs_without_a_caller()
    {
        var behavior = Behavior<AnonymousCmd>(new ScopedExecutionContext());

        var result = await behavior.Handle(
            new AnonymousCmd(), CancellationToken.None, Reached);

        Assert.Equal("reached", result.Value);
    }

    /// <summary>
    /// The marker declares "no caller required", not "no caller permitted". A
    /// signed-in user following an activation link must not be refused.
    /// </summary>
    [Fact]
    public async Task An_anonymous_command_also_runs_with_a_caller_established()
    {
        var context = new ScopedExecutionContext();
        context.Establish(UserId.New(), ActorType.Human);

        var behavior = Behavior<AnonymousCmd>(context);

        var result = await behavior.Handle(
            new AnonymousCmd(), CancellationToken.None, Reached);

        Assert.Equal("reached", result.Value);
    }

    /// <summary>
    /// Declaring both markers is a configuration mistake, not a business
    /// failure: an anonymous command has no actor whose type could be checked.
    /// Without the guard this surfaces as an InvalidOperationException from an
    /// unestablished context, which reads like an infrastructure fault.
    /// </summary>
    [Fact]
    public async Task Declaring_both_anonymous_and_human_only_is_refused_clearly()
    {
        var behavior = new HumanActorBehavior<Contradictory, TestResult>(
            new ScopedExecutionContext());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.Handle(
                new Contradictory(), CancellationToken.None, Reached));

        Assert.Contains("IAnonymousCommand", failure.Message);
        Assert.Contains("IHumanActorOnlyCommand", failure.Message);
    }

    private static AuthenticationBehavior<TCommand, TestResult> Behavior<TCommand>(
        IExecutionContext context)
        where TCommand : ICommand<TestResult>
        => new(context);

    private static Task<TestResult> Reached(CancellationToken _)
        => Task.FromResult(new TestResult("reached"));

    private sealed record TestResult(string Value);

    private sealed record AuthenticatedCommand : ICommand<TestResult>;

    private sealed record AnonymousCmd : IAnonymousCommand<TestResult>;

    private sealed record Contradictory
        : IAnonymousCommand<TestResult>, IHumanActorOnlyCommand<TestResult>;
}
