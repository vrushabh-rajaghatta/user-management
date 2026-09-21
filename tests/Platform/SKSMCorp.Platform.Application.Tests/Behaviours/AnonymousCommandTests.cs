using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Behaviors;
using SKSMCorp.Platform.Application.Execution;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Tests.Behaviors;

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
    /// signed-in user asking for a password-reset email must not be refused.
    /// The exception is the narrower bearer marker, below.
    /// </summary>
    [Fact]
    public async Task An_anonymous_command_also_runs_with_a_caller_established()
    {
        var context = new ScopedExecutionContext();
        context.Establish(UserId.New(), ActorType.Human, TestActorIdentity.Human());

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

    [Fact]
    public async Task A_bearer_authenticated_command_runs_without_a_caller()
    {
        var behavior = Behavior<BearerCmd>(new ScopedExecutionContext());

        var result = await behavior.Handle(
            new BearerCmd(), CancellationToken.None, Reached);

        Assert.Equal("reached", result.Value);
    }

    /// <summary>
    /// A bearer-authenticated identity-establishing command may execute only
    /// when no caller is already established. Refused before the command
    /// starts: next is never called, so nothing downstream — the transaction,
    /// the handler, the audit pipeline — is reached.
    /// </summary>
    [Fact]
    public async Task A_bearer_authenticated_command_is_refused_before_it_starts_when_a_caller_is_established()
    {
        var context = new ScopedExecutionContext();
        context.Establish(UserId.New(), ActorType.Human, TestActorIdentity.Human());

        var behavior = Behavior<BearerCmd>(context);

        var reached = false;

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => behavior.Handle(
                new BearerCmd(),
                CancellationToken.None,
                _ =>
                {
                    reached = true;

                    return Reached(CancellationToken.None);
                }));

        Assert.False(reached, "A refused command must never reach the rest of the pipeline.");
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

    private sealed record BearerCmd : IBearerAuthenticatedCommand<TestResult>;

    private sealed record Contradictory
        : IAnonymousCommand<TestResult>, IHumanActorOnlyCommand<TestResult>;
}
