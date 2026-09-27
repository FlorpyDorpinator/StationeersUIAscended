namespace FeedbackRelay;

/// <summary>
/// In-memory sliding-window limiter: at most <c>limit</c> hits per key in any <c>window</c>.
/// The in-app backstop behind the Cloudflare edge rule (plan §2.4) - so a missing or mis-set
/// edge rule never means "unlimited". Rejected attempts are NOT recorded, so hammering does
/// not extend a lockout; the oldest hit ageing out always frees a slot.
/// State is per process: a restart forgets it, which is acceptable for a backstop.
/// </summary>
public sealed class SlidingWindowLimiter
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Queue<DateTimeOffset>> _hits = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private DateTimeOffset _lastSweep;

    public SlidingWindowLimiter(int limit, TimeSpan window, TimeProvider? time = null)
    {
        _limit = limit;
        _window = window;
        _time = time ?? TimeProvider.System;
        _lastSweep = _time.GetUtcNow();
    }

    public bool TryAcquire(string key, out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            SweepIfDue(now);

            if (!_hits.TryGetValue(key, out var q))
            {
                q = new Queue<DateTimeOffset>(_limit);
                _hits[key] = q;
            }
            while (q.Count > 0 && now - q.Peek() >= _window) q.Dequeue();

            if (q.Count >= _limit)
            {
                retryAfter = q.Peek() + _window - now;
                if (retryAfter < TimeSpan.FromSeconds(1)) retryAfter = TimeSpan.FromSeconds(1);
                return false;
            }

            q.Enqueue(now);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    /// <summary>Drop keys whose every hit has aged out, at most once a minute, so memory
    /// tracks the last hour's distinct clients rather than every client ever seen.</summary>
    private void SweepIfDue(DateTimeOffset now)
    {
        if (now - _lastSweep < TimeSpan.FromMinutes(1)) return;
        _lastSweep = now;
        var stale = new List<string>();
        foreach (var (key, q) in _hits)
        {
            while (q.Count > 0 && now - q.Peek() >= _window) q.Dequeue();
            if (q.Count == 0) stale.Add(key);
        }
        foreach (var key in stale) _hits.Remove(key);
    }
}

/// <summary>
/// "Same title + description within the window" detector (plan §2.4). Global, not per IP,
/// so the same spam from rotating IPs is caught too.
///
/// Reserve-then-release: a key is reserved atomically BEFORE GitHub is called (so two
/// concurrent identical posts can't both file), kept on success, and released on failure (so
/// the mod's outbox retry of a report that never landed isn't refused as a duplicate). A
/// retry of a report that DID land - e.g. the relay answered after the game gave up waiting -
/// is refused, which is exactly what prevents a double-filed issue.
/// </summary>
public sealed class DedupeWindow
{
    private readonly TimeSpan _window;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private DateTimeOffset _lastSweep;

    public DedupeWindow(TimeSpan window, TimeProvider? time = null)
    {
        _window = window;
        _time = time ?? TimeProvider.System;
        _lastSweep = _time.GetUtcNow();
    }

    public bool TryReserve(string key)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now - _lastSweep >= TimeSpan.FromMinutes(1))
            {
                _lastSweep = now;
                var stale = _seen.Where(kv => now - kv.Value >= _window).Select(kv => kv.Key).ToList();
                foreach (var k in stale) _seen.Remove(k);
            }

            if (_seen.TryGetValue(key, out var at) && now - at < _window) return false;
            _seen[key] = now;
            return true;
        }
    }

    public void Release(string key)
    {
        lock (_gate) _seen.Remove(key);
    }
}

/// <summary>
/// 60-second in-memory cache for GET /v1/status/{n}: repeated tab-opens and scanners cost one
/// GitHub call per issue per minute at most. Caches definite answers (found / not found),
/// never transient failures. Bounded so an enumeration can't grow it without limit.
/// </summary>
public sealed class StatusCache
{
    public sealed record Entry(bool Found, StatusResponse? Status);

    private const int MaxEntries = 4096;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;
    private readonly Dictionary<int, (DateTimeOffset At, Entry Entry)> _entries = new();
    private readonly object _gate = new();

    public StatusCache(TimeSpan ttl, TimeProvider? time = null)
    {
        _ttl = ttl;
        _time = time ?? TimeProvider.System;
    }

    public bool TryGet(int number, out Entry entry)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(number, out var hit) && _time.GetUtcNow() - hit.At < _ttl)
            {
                entry = hit.Entry;
                return true;
            }
            entry = null!;
            return false;
        }
    }

    public void Set(int number, Entry entry)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_entries.Count >= MaxEntries)
            {
                var stale = _entries.Where(kv => now - kv.Value.At >= _ttl).Select(kv => kv.Key).ToList();
                foreach (var k in stale) _entries.Remove(k);
                if (_entries.Count >= MaxEntries) _entries.Clear();
            }
            _entries[number] = (now, entry);
        }
    }
}
