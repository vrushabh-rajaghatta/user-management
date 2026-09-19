using Ligature.Platform.Application.RateLimiting;

namespace Ligature.Platform.Application.Tests.RateLimiting;

/// <summary>
/// The buckets themselves (docs/requirements.md, "Behaviour 11 — rate limiting
/// the anonymous commands", Admission and Storage). Time is passed in, so the
/// window is exercised to the tick without sleeping (RL-11).
/// </summary>
public sealed class RateLimitStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly RateLimitRule ByAddress =
        new("test", RateLimitKeyKind.Address, 3, TimeSpan.FromMinutes(10));

    private static readonly RateLimitRule ByClient =
        new("test", RateLimitKeyKind.ClientAddress, 3, TimeSpan.FromMinutes(30));

    private static RateLimitKey Address(string value = "ada") => new(ByAddress, value);

    private static RateLimitKey Client(string value = "203.0.113.7") => new(ByClient, value);

    [Fact]
    public void A_bucket_admits_exactly_its_limit_within_the_window()
    {
        var store = new RateLimitStore();

        for (var i = 0; i < 3; i++)
            Assert.True(store.TryAdmit([Address()], T0.AddSeconds(i)).Admitted);

        var refused = store.TryAdmit([Address()], T0.AddSeconds(3));

        Assert.False(refused.Admitted);
        Assert.Equal([RateLimitKeyKind.Address], refused.Exhausted);
    }

    [Fact]
    public void Different_values_and_different_rules_are_different_buckets()
    {
        var store = new RateLimitStore();
        var otherCommand = new RateLimitRule("other", RateLimitKeyKind.Address, 3, TimeSpan.FromMinutes(10));

        for (var i = 0; i < 3; i++)
            Assert.True(store.TryAdmit([Address("ada")], T0).Admitted);

        Assert.True(store.TryAdmit([Address("grace")], T0).Admitted);
        Assert.True(store.TryAdmit([new RateLimitKey(otherCommand, "ada")], T0).Admitted);
        Assert.False(store.TryAdmit([Address("ada")], T0).Admitted);
    }

    /// <summary>RL-9: an AND-gate, and a refusal is counted in NO bucket.</summary>
    [Fact]
    public void A_request_refused_by_one_bucket_is_counted_in_none()
    {
        var store = new RateLimitStore();

        for (var i = 0; i < 3; i++)
            Assert.True(store.TryAdmit([Address()], T0).Admitted);

        // The address bucket is exhausted; the client bucket has all 3 left.
        var refused = store.TryAdmit([Address(), Client()], T0);

        Assert.False(refused.Admitted);
        Assert.Equal([RateLimitKeyKind.Address], refused.Exhausted);

        // Still 3 in the client bucket: the refusal took none of them.
        for (var i = 0; i < 3; i++)
            Assert.True(store.TryAdmit([Client()], T0).Admitted);

        Assert.False(store.TryAdmit([Client()], T0).Admitted);
    }

    [Fact]
    public void An_admitted_request_is_counted_in_every_applicable_bucket()
    {
        var store = new RateLimitStore();

        for (var i = 0; i < 3; i++)
            Assert.True(store.TryAdmit([Address(), Client()], T0).Admitted);

        Assert.False(store.TryAdmit([Address()], T0).Admitted);
        Assert.False(store.TryAdmit([Client()], T0).Admitted);
    }

    /// <summary>RL-10: with one request of room left, concurrency admits exactly one.</summary>
    [Fact]
    public async Task Concurrent_requests_at_the_limit_admit_exactly_the_room_left()
    {
        var store = new RateLimitStore();

        Assert.True(store.TryAdmit([Address()], T0).Admitted);
        Assert.True(store.TryAdmit([Address()], T0).Admitted);

        using var start = new ManualResetEventSlim(false);

        var attempts = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return store.TryAdmit([Address(), Client()], T0).Admitted;
            }))
            .ToArray();

        start.Set();

        var admitted = (await Task.WhenAll(attempts)).Count(x => x);

        Assert.Equal(1, admitted);

        // And the one admission is the only one the client bucket saw.
        Assert.True(store.TryAdmit([Client()], T0).Admitted);
        Assert.True(store.TryAdmit([Client()], T0).Admitted);
        Assert.False(store.TryAdmit([Client()], T0).Admitted);
    }

    /// <summary>
    /// RL-10 under sustained contention: many threads, many rounds, one bucket
    /// and one shared second bucket. The admitted total is exactly the limit
    /// every time — a check-then-count that was not atomic would overshoot, or
    /// corrupt the buckets outright.
    /// </summary>
    [Fact]
    public async Task Under_heavy_contention_a_bucket_admits_exactly_its_limit()
    {
        var wide = new RateLimitRule("contention", RateLimitKeyKind.Address, 50, TimeSpan.FromHours(1));
        var shared = new RateLimitRule("contention", RateLimitKeyKind.ClientAddress, 50, TimeSpan.FromHours(1));

        for (var round = 0; round < 20; round++)
        {
            var store = new RateLimitStore();
            using var start = new ManualResetEventSlim(false);

            var workers = Enumerable.Range(0, Environment.ProcessorCount * 2)
                .Select(worker => Task.Factory.StartNew(
                    () =>
                    {
                        start.Wait();
                        var admitted = 0;

                        for (var i = 0; i < 200; i++)
                        {
                            RateLimitKey[] keys =
                            [
                                new(wide, $"round-{round}"),
                                new(shared, $"peer-{round}"),
                                new(wide, $"noise-{worker}-{i}"),
                            ];

                            if (store.TryAdmit(keys, T0).Admitted)
                                admitted++;
                        }

                        return admitted;
                    },
                    TaskCreationOptions.LongRunning))
                .ToArray();

            start.Set();

            Assert.Equal(50, (await Task.WhenAll(workers)).Sum());
        }
    }

    /// <summary>RL-11: exactly one window after the oldest counted admission, not before.</summary>
    [Fact]
    public void The_window_slides_from_each_admission()
    {
        var store = new RateLimitStore();

        Assert.True(store.TryAdmit([Address()], T0).Admitted);
        Assert.True(store.TryAdmit([Address()], T0.AddMinutes(4)).Admitted);
        Assert.True(store.TryAdmit([Address()], T0.AddMinutes(5)).Admitted);

        // The first admission counts until T0 + 10 minutes, exclusive.
        Assert.False(store.TryAdmit([Address()], T0.AddMinutes(10).AddTicks(-1)).Admitted);
        Assert.True(store.TryAdmit([Address()], T0.AddMinutes(10)).Admitted);

        // Now the oldest counted is T0 + 4.
        Assert.False(store.TryAdmit([Address()], T0.AddMinutes(14).AddTicks(-1)).Admitted);
        Assert.True(store.TryAdmit([Address()], T0.AddMinutes(14)).Admitted);
    }

    /// <summary>RL-11: refusals do not extend the refusal.</summary>
    [Fact]
    public void A_caller_that_keeps_being_refused_is_admitted_when_the_window_allows()
    {
        var store = new RateLimitStore();

        for (var i = 0; i < 3; i++)
            Assert.True(store.TryAdmit([Address()], T0).Admitted);

        for (var minute = 1; minute < 10; minute++)
            Assert.False(store.TryAdmit([Address()], T0.AddMinutes(minute)).Admitted);

        Assert.True(store.TryAdmit([Address()], T0.AddMinutes(10)).Admitted);
    }

    [Fact]
    public void Retry_after_is_the_time_until_the_exhausted_bucket_has_room()
    {
        var store = new RateLimitStore();

        store.TryAdmit([Address()], T0);
        store.TryAdmit([Address()], T0.AddMinutes(2));
        store.TryAdmit([Address()], T0.AddMinutes(3));

        var refused = store.TryAdmit([Address(), Client()], T0.AddMinutes(4));

        // The oldest admission (T0) leaves at T0 + 10.
        Assert.Equal(TimeSpan.FromMinutes(6), refused.RetryAfter);
    }

    /// <summary>
    /// RL-15: with both buckets exhausted, the LATER of the two — a retry is
    /// admitted only when every exhausted bucket has room.
    /// </summary>
    [Fact]
    public void Retry_after_with_two_exhausted_buckets_is_the_later_of_the_two()
    {
        var store = new RateLimitStore();

        // Address bucket (10 min) full from T0; client bucket (30 min) full from T0 + 1.
        for (var i = 0; i < 3; i++)
            store.TryAdmit([Address()], T0);

        for (var i = 0; i < 3; i++)
            store.TryAdmit([Client()], T0.AddMinutes(1));

        var refused = store.TryAdmit([Address(), Client()], T0.AddMinutes(2));

        Assert.False(refused.Admitted);
        Assert.Equal(TimeSpan.FromMinutes(29), refused.RetryAfter);
        Assert.Equal(
            new HashSet<RateLimitKeyKind> { RateLimitKeyKind.Address, RateLimitKeyKind.ClientAddress },
            refused.Exhausted.ToHashSet());

        // And that is the boundary: refused just before it, admitted at it.
        Assert.False(store.TryAdmit([Address(), Client()], T0.AddMinutes(31).AddTicks(-1)).Admitted);
        Assert.True(store.TryAdmit([Address(), Client()], T0.AddMinutes(31)).Admitted);
    }

    [Fact]
    public void An_admission_reports_no_retry_and_nothing_exhausted()
    {
        var admitted = new RateLimitStore().TryAdmit([Address(), Client()], T0);

        Assert.True(admitted.Admitted);
        Assert.Equal(TimeSpan.Zero, admitted.RetryAfter);
        Assert.Empty(admitted.Exhausted);
    }

    /// <summary>RL-20: the store keeps nothing for callers that have gone quiet.</summary>
    [Fact]
    public void A_key_whose_admissions_have_all_left_the_window_is_removed()
    {
        var store = new RateLimitStore();

        store.TryAdmit([Address("ada"), Client()], T0);
        store.TryAdmit([Address("grace")], T0.AddMinutes(5));

        Assert.Equal(3, store.TrackedKeyCount);

        // At T0 + 31 every admission has left its window; the next admission
        // is where removal happens, and only its own key remains.
        store.TryAdmit([Address("linus")], T0.AddMinutes(31));

        Assert.Equal(1, store.TrackedKeyCount);
    }

    [Fact]
    public void A_request_with_no_keys_is_admitted()
    {
        var store = new RateLimitStore();

        for (var i = 0; i < 10; i++)
            Assert.True(store.TryAdmit([], T0).Admitted);

        Assert.Equal(0, store.TrackedKeyCount);
    }
}
