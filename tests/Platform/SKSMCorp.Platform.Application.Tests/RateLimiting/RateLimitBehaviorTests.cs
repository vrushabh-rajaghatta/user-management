using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Behaviors;
using SKSMCorp.Platform.Application.RateLimiting;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Tests.RateLimiting;

/// <summary>
/// Behaviour 11 in the pipeline (docs/requirements.md, "Behaviour 11",
/// Admission). The behaviour names no command: it tests the marker, normalises
/// each declared value by its rule's kind, reads the clock once, and either
/// lets the command start or refuses it before anything else runs.
/// </summary>
public sealed class RateLimitBehaviorTests
{
    private static readonly RateLimitRule ByAddress =
        new("test", RateLimitKeyKind.Address, 2, TimeSpan.FromMinutes(10));

    private static readonly RateLimitRule ByClient =
        new("test", RateLimitKeyKind.ClientAddress, 2, TimeSpan.FromMinutes(1));

    private sealed record LimitedCommand(string? Address, string? ClientAddress)
        : IAnonymousCommand<string>, IRateLimitedCommand
    {
        public IReadOnlyList<RateLimitSubject> RateLimitSubjects =>
            [new(ByAddress, Address), new(ByClient, ClientAddress)];
    }

    private sealed record UnlimitedCommand : ICommand<string>;

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));

    private readonly RateLimitStore _store = new();

    private int _reached;

    private Task<string> RunAsync<TCommand>(TCommand command)
        where TCommand : ICommand<string>
        => new RateLimitBehavior<TCommand, string>(_store, _clock).Handle(
            command,
            CancellationToken.None,
            _ =>
            {
                _reached++;
                return Task.FromResult("ran");
            });

    [Fact]
    public async Task A_command_that_declares_no_limits_passes_untouched()
    {
        for (var i = 0; i < 5; i++)
            Assert.Equal("ran", await RunAsync(new UnlimitedCommand()));

        Assert.Equal(5, _reached);
        Assert.Equal(0, _store.TrackedKeyCount);
    }

    [Fact]
    public async Task An_admitted_command_reaches_the_rest_of_the_pipeline()
    {
        Assert.Equal("ran", await RunAsync(new LimitedCommand("ada", "203.0.113.7")));
        Assert.Equal(1, _reached);
        Assert.Equal(2, _store.TrackedKeyCount);
    }

    /// <summary>RL-12, at the behaviour: a refusal never calls next.</summary>
    [Fact]
    public async Task A_refused_command_goes_no_further_and_says_why_only_to_the_host()
    {
        await RunAsync(new LimitedCommand("ada", "203.0.113.7"));
        await RunAsync(new LimitedCommand("ada", "203.0.113.8"));

        var refused = await Assert.ThrowsAsync<RateLimitExceededException>(
            () => RunAsync(new LimitedCommand("ada", "203.0.113.9")));

        Assert.Equal(2, _reached);
        Assert.Equal(TimeSpan.FromMinutes(10), refused.RetryAfter);
        Assert.Equal(["address"], refused.ExhaustedKeyKinds);
        Assert.Equal("203.0.113.9", refused.ClientAddress);

        // The typed address is not carried on the exception at all.
        Assert.DoesNotContain("ada", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_exhausted_client_address_is_reported_as_ip()
    {
        await RunAsync(new LimitedCommand("ada", "203.0.113.7"));
        await RunAsync(new LimitedCommand("grace", "203.0.113.7"));

        var refused = await Assert.ThrowsAsync<RateLimitExceededException>(
            () => RunAsync(new LimitedCommand("linus", "203.0.113.7")));

        Assert.Equal(["ip"], refused.ExhaustedKeyKinds);
    }

    /// <summary>RL-7: the behaviour normalises, so a command cannot forget to.</summary>
    [Fact]
    public async Task Spellings_of_one_address_share_a_bucket()
    {
        await RunAsync(new LimitedCommand("Ada", null));
        await RunAsync(new LimitedCommand(" ada ", null));

        await Assert.ThrowsAsync<RateLimitExceededException>(
            () => RunAsync(new LimitedCommand("ADA", null)));
    }

    /// <summary>RL-19: no resolvable address means no IP gate, not a shared bucket.</summary>
    [Fact]
    public async Task No_client_address_means_no_ip_gate_but_the_address_gate_still_applies()
    {
        for (var i = 0; i < 10; i++)
            await RunAsync(new LimitedCommand($"user-{i}", null));

        Assert.Equal(10, _reached);

        await RunAsync(new LimitedCommand("ada", null));
        await RunAsync(new LimitedCommand("ada", null));

        var refused = await Assert.ThrowsAsync<RateLimitExceededException>(
            () => RunAsync(new LimitedCommand("ada", null)));

        Assert.Equal(["address"], refused.ExhaustedKeyKinds);
        Assert.Null(refused.ClientAddress);
    }

    [Fact]
    public async Task A_blank_address_is_limited_by_ip_alone()
    {
        await RunAsync(new LimitedCommand("", "203.0.113.7"));
        await RunAsync(new LimitedCommand("  ", "203.0.113.7"));

        var refused = await Assert.ThrowsAsync<RateLimitExceededException>(
            () => RunAsync(new LimitedCommand(null, "203.0.113.7")));

        Assert.Equal(["ip"], refused.ExhaustedKeyKinds);
    }

    /// <summary>The window is read from IClock.</summary>
    [Fact]
    public async Task The_clock_decides_when_a_bucket_has_room_again()
    {
        await RunAsync(new LimitedCommand("ada", null));
        await RunAsync(new LimitedCommand("ada", null));

        _clock.UtcNow = _clock.UtcNow.AddMinutes(10).AddTicks(-1);

        await Assert.ThrowsAsync<RateLimitExceededException>(
            () => RunAsync(new LimitedCommand("ada", null)));

        _clock.UtcNow = _clock.UtcNow.AddTicks(1);

        Assert.Equal("ran", await RunAsync(new LimitedCommand("ada", null)));
    }
}
