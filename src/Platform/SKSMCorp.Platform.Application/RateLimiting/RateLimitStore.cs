namespace SKSMCorp.Platform.Application.RateLimiting;

/// <summary>
/// The in-memory buckets (B10): one instance per host process, registered as a
/// singleton. Correct for the single-process deployment (docs/architecture.md
/// §4); a restart, or a second instance, starts empty. More than one instance
/// would need a shared store — an explicit non-decision.
///
/// <para><b>The window slides.</b> An admitted request counts against a bucket
/// for exactly one window from the instant it was admitted; a bucket has room
/// while fewer than its limit fall inside the last window.</para>
///
/// <para><b>Admission is atomic across the keys.</b> Every key is judged, and
/// then either every key counts the request or none does, under one lock — so
/// concurrent requests at the limit admit exactly the room left, and a refusal
/// takes nothing from the buckets that still had room.</para>
///
/// <para><b>Bounded.</b> A key holds at most its limit's instants, and a key
/// whose instants have all left the window is removed during a later admission
/// — no background timer.</para>
/// </summary>
public sealed class RateLimitStore
{
    /// <summary>A full sweep runs at most this often, driven by admissions.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();

    private readonly Dictionary<RateLimitKey, Bucket> _buckets = [];

    private DateTimeOffset _nextSweep = DateTimeOffset.MinValue;

    public RateLimitDecision TryAdmit(IReadOnlyList<RateLimitKey> keys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(keys);

        lock (_gate)
        {
            Sweep(now);

            var exhausted = new List<RateLimitKeyKind>();
            var availableAt = DateTimeOffset.MinValue;

            foreach (var key in keys)
            {
                if (!_buckets.TryGetValue(key, out var bucket))
                    continue;

                bucket.Expire(key.Rule.Window, now);

                if (bucket.Count < key.Rule.Limit)
                    continue;

                if (!exhausted.Contains(key.Rule.Kind))
                    exhausted.Add(key.Rule.Kind);

                // A retry is admitted only when EVERY exhausted bucket has
                // room, so the answer is the latest of their instants.
                var at = bucket.Oldest + key.Rule.Window;

                if (at > availableAt)
                    availableAt = at;
            }

            if (exhausted.Count > 0)
                return new RateLimitDecision(false, availableAt - now, exhausted);

            foreach (var key in keys)
            {
                if (!_buckets.TryGetValue(key, out var bucket))
                    _buckets[key] = bucket = new Bucket();

                bucket.Add(now);
            }

            return RateLimitDecision.Admit;
        }
    }

    /// <summary>How many keys the store currently holds.</summary>
    public int TrackedKeyCount
    {
        get
        {
            lock (_gate)
                return _buckets.Count;
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;

        foreach (var (key, bucket) in _buckets.ToList())
        {
            bucket.Expire(key.Rule.Window, now);

            if (bucket.Count == 0)
                _buckets.Remove(key);
        }
    }

    /// <summary>The admission instants still inside the window, oldest first.</summary>
    private sealed class Bucket
    {
        private readonly Queue<DateTimeOffset> _admitted = new();

        public int Count => _admitted.Count;

        public DateTimeOffset Oldest => _admitted.Peek();

        public void Add(DateTimeOffset at) => _admitted.Enqueue(at);

        /// <summary>An instant stops counting exactly one window after it.</summary>
        public void Expire(TimeSpan window, DateTimeOffset now)
        {
            while (_admitted.Count > 0 && _admitted.Peek() + window <= now)
                _admitted.Dequeue();
        }
    }
}
