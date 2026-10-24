using System.Diagnostics;

namespace CS2M.Lobby;

// None of these are thread safe; UdpService calls them under its lock. All of them have a fixed size
// from the start, so a flood of spoofed source addresses can't grow the heap.

/// <summary>A token bucket: <c>perSecond</c> on average, up to <c>burst</c> at once.</summary>
public sealed class TokenBucket
{
    private readonly double _rate;
    private readonly double _burst;
    private double _tokens;
    private long _last;

    public TokenBucket(double perSecond, double burst)
    {
        _rate = perSecond;
        _burst = Math.Max(1, burst);
        _tokens = _burst;
        _last = Stopwatch.GetTimestamp();
    }

    public bool TryTake()
    {
        long now = Stopwatch.GetTimestamp();
        _tokens = Math.Min(_burst, _tokens + (now - _last) * _rate / Stopwatch.Frequency);
        _last = now;
        if (_tokens < 1)
        {
            return false;
        }

        _tokens -= 1;
        return true;
    }
}

/// <summary>
///     Fixed window counters per key in a fixed-size table (a count-min sketch with two rows): a key's count is
///     the smaller of its two slots, so a collision can only make a key look busier, never let it through.
/// </summary>
public sealed class WindowLimiter
{
    private readonly int _limit;
    private readonly long _window;
    private readonly int _mask;
    private readonly long[] _start;
    private readonly int[] _count;

    /// <param name="slots">Per row, a power of two.</param>
    public WindowLimiter(int limit, TimeSpan window, int slots = 16384)
    {
        if (slots <= 0 || (slots & (slots - 1)) != 0)
        {
            throw new ArgumentException("slots must be a power of two", nameof(slots));
        }

        _limit = limit;
        _window = (long)(window.TotalSeconds * Stopwatch.Frequency);
        _mask = slots - 1;
        _start = new long[slots * 2];
        _count = new int[slots * 2];
    }

    public bool Allow(ulong key)
    {
        long now = Stopwatch.GetTimestamp();
        int a = Bump(Net.Slot(key, 0, _mask), now);
        int b = Bump(_mask + 1 + Net.Slot(key, 1, _mask), now);
        return Math.Min(a, b) <= _limit;
    }

    private int Bump(int i, long now)
    {
        if (now - _start[i] > _window)
        {
            _start[i] = now;
            _count[i] = 0;
        }

        if (_count[i] < int.MaxValue)
        {
            _count[i]++;
        }

        return _count[i];
    }
}

/// <summary>
///     How many distinct values (hashed strings) each key used in a window, up to <c>maxDistinct</c>, in a
///     fixed-size table. A key lives in one of two slots; when both are taken by other live keys the older one
///     is replaced.
/// </summary>
public sealed class DistinctLimiter
{
    private readonly int _max;
    private readonly long _window;
    private readonly int _mask;
    private readonly ulong[] _keys;
    private readonly long[] _start;
    private readonly int[] _count;
    private readonly ulong[] _values;

    public DistinctLimiter(int maxDistinct, TimeSpan window, int slots)
    {
        if (slots <= 0 || (slots & (slots - 1)) != 0)
        {
            throw new ArgumentException("slots must be a power of two", nameof(slots));
        }

        _max = maxDistinct;
        _window = (long)(window.TotalSeconds * Stopwatch.Frequency);
        _mask = slots - 1;
        _keys = new ulong[slots];
        _start = new long[slots];
        _count = new int[slots];
        _values = new ulong[slots * maxDistinct];
    }

    /// <summary>Whether <paramref name="value" /> is already counted for the key, or there is room for it.</summary>
    public bool Check(ulong key, ulong value)
    {
        int slot = Find(key, Stopwatch.GetTimestamp(), false);
        return slot < 0 || Contains(slot, value) || _count[slot] < _max;
    }

    public void Record(ulong key, ulong value)
    {
        int slot = Find(key, Stopwatch.GetTimestamp(), true);
        if (!Contains(slot, value) && _count[slot] < _max)
        {
            _values[slot * _max + _count[slot]] = value;
            _count[slot]++;
        }
    }

    private bool Contains(int slot, ulong value)
    {
        for (int i = 0; i < _count[slot]; i++)
        {
            if (_values[slot * _max + i] == value)
            {
                return true;
            }
        }

        return false;
    }

    private bool Live(int slot, long now)
    {
        return _start[slot] != 0 && now - _start[slot] <= _window;
    }

    private int Find(ulong key, long now, bool create)
    {
        int a = Net.Slot(key, 0, _mask), b = Net.Slot(key, 1, _mask);
        if (_keys[a] == key && Live(a, now))
        {
            return a;
        }

        if (_keys[b] == key && Live(b, now))
        {
            return b;
        }

        if (!create)
        {
            return -1;
        }

        int slot = !Live(a, now) ? a : !Live(b, now) ? b : _start[a] <= _start[b] ? a : b;
        _keys[slot] = key;
        _start[slot] = now;
        _count[slot] = 0;
        return slot;
    }
}

/// <summary>What the UDP side did, logged once a minute instead of a line per packet.</summary>
public sealed class UdpStats
{
    public long Packets, OverBudget, RateLimited, Undecodable, UnknownType;
    public long Added, Refreshed, Invalid, TokenTaken, Full, Ignored, Closed, CloseRefused, DetailsRefused;
    public long Introduced, IntroUnknown, IntroLimited, IntroSessionLimited;
    public long PortChecks, PortCheckRefused, PortCheckExpired, SocketErrors, LibraryErrors;

    /// <summary>A summary line of everything that happened since the last call, or null if nothing did.</summary>
    public string? Drain()
    {
        var parts = new List<string>();

        void Add(string name, ref long value)
        {
            if (value != 0)
            {
                parts.Add($"{name} {value}");
                value = 0;
            }
        }

        Add("packets", ref Packets);
        Add("over budget", ref OverBudget);
        Add("rate limited", ref RateLimited);
        Add("undecodable", ref Undecodable);
        Add("unknown type", ref UnknownType);
        Add("added", ref Added);
        Add("refreshed", ref Refreshed);
        Add("invalid", ref Invalid);
        Add("token taken", ref TokenTaken);
        Add("full", ref Full);
        Add("port change held", ref Ignored);
        Add("closed", ref Closed);
        Add("close refused", ref CloseRefused);
        Add("details refused", ref DetailsRefused);
        Add("introduced", ref Introduced);
        Add("intro unknown", ref IntroUnknown);
        Add("intro limited", ref IntroLimited);
        Add("intro session cap", ref IntroSessionLimited);
        Add("port checks", ref PortChecks);
        Add("port check refused", ref PortCheckRefused);
        Add("port check expired", ref PortCheckExpired);
        Add("socket errors", ref SocketErrors);
        Add("malformed (LiteNetLib)", ref LibraryErrors);
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
