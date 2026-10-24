using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CS2M.Networking
{
    /// <summary>
    ///     Host: counts failed join checks (wrong password, version or mod mismatch, bad name) per address, and
    ///     refuses that address for a while after too many. The block doubles each time, up to an hour.
    ///     Main thread only. Addresses are kept in memory only and never logged.
    /// </summary>
    public class JoinGuard
    {
        private const int MaxFailures = 8;
        private const double WindowSeconds = 10 * 60;
        private const double FirstBlockSeconds = 2 * 60;
        private const double MaxBlockSeconds = 60 * 60;
        private const int MaxAddresses = 1024;

        private class Entry
        {
            public int Failures;
            public double WindowStart;
            public double BlockedUntil;
            public int Blocks;
        }

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        private double Now => _clock.Elapsed.TotalSeconds;

        public bool IsBlocked(string address, out int minutes)
        {
            minutes = 0;
            if (address == null || !_entries.TryGetValue(address, out Entry entry) || entry.BlockedUntil <= Now)
            {
                return false;
            }

            minutes = Math.Max(1, (int)Math.Ceiling((entry.BlockedUntil - Now) / 60));
            return true;
        }

        /// <summary>
        ///     Records a failed check. Returns true when the address is now blocked.
        /// </summary>
        public bool RecordFailure(string address)
        {
            if (address == null)
            {
                return false;
            }

            double now = Now;
            if (!_entries.TryGetValue(address, out Entry entry))
            {
                Prune(now);
                entry = new Entry { WindowStart = now };
                _entries[address] = entry;
            }

            if (now - entry.WindowStart > WindowSeconds)
            {
                entry.Failures = 0;
                entry.WindowStart = now;
            }

            entry.Failures++;
            if (entry.Failures < MaxFailures)
            {
                return false;
            }

            entry.Failures = 0;
            entry.WindowStart = now;
            entry.BlockedUntil = now + Math.Min(MaxBlockSeconds, FirstBlockSeconds * Math.Pow(2, entry.Blocks));
            entry.Blocks++;
            return true;
        }

        /// <summary>
        ///     A successful join from the address clears its failures (not an active block).
        /// </summary>
        public void RecordSuccess(string address)
        {
            if (address != null && _entries.TryGetValue(address, out Entry entry) && entry.BlockedUntil <= Now)
            {
                _entries.Remove(address);
            }
        }

        private void Prune(double now)
        {
            if (_entries.Count < MaxAddresses)
            {
                return;
            }

            foreach (string key in _entries
                         .Where(pair => pair.Value.BlockedUntil <= now && now - pair.Value.WindowStart > WindowSeconds)
                         .Select(pair => pair.Key).ToList())
            {
                _entries.Remove(key);
            }

            if (_entries.Count >= MaxAddresses)
            {
                // Still full of live entries: drop the oldest windows rather than grow without bound
                foreach (string key in _entries.OrderBy(pair => pair.Value.WindowStart).Take(MaxAddresses / 4)
                             .Select(pair => pair.Key).ToList())
                {
                    _entries.Remove(key);
                }
            }
        }
    }
}
