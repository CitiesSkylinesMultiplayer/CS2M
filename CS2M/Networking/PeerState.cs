using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CS2M.Networking
{
    /// <summary>
    ///     What the host knows about one connection, kept in NetPeer.Tag from the moment the connection
    ///     request is accepted. LiteNetLib reuses peer ids, so players are never looked up by peer id.
    /// </summary>
    public class PeerState
    {
        /// <summary>
        ///     Failed join attempts allowed on one connection before it is closed.
        /// </summary>
        public const int MaxFailedChecks = 5;

        /// <summary>
        ///     Bad packets (malformed, too big, not allowed, throwing) allowed before the peer is disconnected.
        /// </summary>
        public const int MaxStrikes = 20;

        public PeerState(string address)
        {
            Address = address;
            Connected.Start();
        }

        /// <summary>
        ///     The remote address, only for the per-address limits and bans. Never logged or shown.
        /// </summary>
        public string Address { get; }

        /// <summary>
        ///     Time since the connection was accepted (registration deadline).
        /// </summary>
        public Stopwatch Connected { get; } = new Stopwatch();

        /// <summary>
        ///     The player, once the PreconditionsCheck passed. Null while the peer is pending.
        /// </summary>
        public RemotePlayer Player { get; set; }

        public bool Registered => Player != null;

        public int FailedChecks { get; set; }

        public int Strikes { get; set; }

        /// <summary>
        ///     Messages dropped for going over <see cref="Messages" />.
        /// </summary>
        public int RateDrops { get; set; }

        /// <summary>
        ///     All commands: cursors at 20/s plus bursts of tool actions fit easily.
        /// </summary>
        public TokenBucket Messages { get; } = new TokenBucket(600, 150);

        /// <summary>
        ///     Chat: 5 messages per 10 seconds.
        /// </summary>
        public TokenBucket Chat { get; } = new TokenBucket(5, 0.5);

        /// <summary>
        ///     Resync requests from this player (each one costs the host a full save).
        /// </summary>
        public Stopwatch LastResync { get; } = new Stopwatch();
    }

    /// <summary>
    ///     A token bucket: holds up to Capacity tokens and refills RefillPerSecond tokens a second.
    /// </summary>
    public class TokenBucket
    {
        private readonly double _capacity;
        private readonly double _refillPerSecond;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _tokens;
        private double _last;

        public TokenBucket(double capacity, double refillPerSecond)
        {
            _capacity = capacity;
            _refillPerSecond = refillPerSecond;
            _tokens = capacity;
        }

        public bool TryTake(double cost = 1)
        {
            double now = _clock.Elapsed.TotalSeconds;
            _tokens = Math.Min(_capacity, _tokens + (now - _last) * _refillPerSecond);
            _last = now;
            if (_tokens < cost)
            {
                return false;
            }

            _tokens -= cost;
            return true;
        }
    }

    /// <summary>
    ///     Logs a warning at most once per interval for each key, with the number of messages it held back.
    ///     Main thread only.
    /// </summary>
    public static class RateLimitedLog
    {
        private const double IntervalSeconds = 10;
        private const int MaxKeys = 256;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly Dictionary<string, (double last, int suppressed)> Keys =
            new Dictionary<string, (double, int)>();

        public static void Warn(string key, string message)
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (Keys.TryGetValue(key, out (double last, int suppressed) entry) && now - entry.last < IntervalSeconds)
            {
                Keys[key] = (entry.last, entry.suppressed + 1);
                return;
            }

            if (Keys.Count >= MaxKeys)
            {
                Keys.Clear();
            }

            Keys[key] = (now, 0);
            Log.Warn(entry.suppressed > 0 ? $"{message} ({entry.suppressed} more like this not logged)" : message);
        }
    }
}
