using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;

namespace CS2M.BaseGame
{
    /// <summary>
    ///     Limits and checks for what comes in from the network. Anything a peer sends is untrusted: arrays can be
    ///     null, short or huge, floats NaN or far off the map, strings megabytes long.
    /// </summary>
    internal static class Inbound
    {
        // ---------- limits ----------

        /// <summary>Largest |x| and |z| of a world position (the playable map is about +-7.2 km).</summary>
        public const float MapExtent = 15000f;

        /// <summary>Largest |y| of a world position.</summary>
        public const float HeightExtent = 10000f;

        /// <summary>Largest magnitude of any other float (offsets, scales, elevations, rotations).</summary>
        public const float MaxValue = 1000000f;

        public const int MaxNameLength = 64;
        public const int MaxPrefabNameLength = 256;

        public const int MaxRefsPerCommand = 2000;
        public const int MaxDefinitionsPerCommand = 512;
        public const int MaxCoursesPerCommand = 512;
        public const int MaxAreasPerCommand = 256;
        public const int MaxAreaNodes = 1024;
        public const int MaxStampsPerCommand = 128;
        public const int MaxWaypointsPerLine = 1024;
        public const int MaxPoliciesPerCommand = 64;
        public const int MaxWaterChangesPerCommand = 64;
        public const int MaxSubReplacements = 32;
        public const int MaxColorSets = 16;
        public const int MaxListEntries = 1024;
        public const int MaxZoneCells = 4096;

        // ---------- rate-limited warnings ----------

        private const long WarnIntervalMs = 5000;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly Dictionary<string, long> LastWarn = new Dictionary<string, long>();
        private static readonly Dictionary<string, int> Suppressed = new Dictionary<string, int>();

        /// <summary>
        ///     Logs a warning at most once every few seconds per key, so a flood can't flood the log as well.
        /// </summary>
        public static void Warn(string key, string message)
        {
            int skipped;
            lock (LastWarn)
            {
                long now = Clock.ElapsedMilliseconds;
                if (LastWarn.TryGetValue(key, out long last) && now - last < WarnIntervalMs)
                {
                    Suppressed.TryGetValue(key, out skipped);
                    Suppressed[key] = skipped + 1;
                    return;
                }

                LastWarn[key] = now;
                Suppressed.TryGetValue(key, out skipped);
                Suppressed[key] = 0;
            }

            BaseGameLog.Warn(skipped > 0 ? $"{message} ({skipped} more like it not logged)" : message);
        }

        /// <summary>
        ///     Logs an exception thrown while applying a received command, so one bad command can't stop the rest.
        /// </summary>
        public static void Failed(string what, Exception e)
        {
            Warn("failed:" + what, $"{what}: a received command could not be applied: {e.GetType().Name}: {e.Message}");
        }

        // ---------- values ----------

        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        // float.MinValue/MaxValue are the game's own "not set" markers (e.g. Game.Areas.Node elevation,
        // object elevation), so they pass; NaN and infinity never do
        public static bool Small(float v) =>
            v == float.MinValue || v == float.MaxValue || (Finite(v) && Math.Abs(v) <= MaxValue);

        public static float Clamp(float v, float min, float max, float fallback = 0f) =>
            Finite(v) ? Math.Max(min, Math.Min(max, v)) : fallback;

        public static bool InMap(float x, float y, float z) =>
            Finite(x) && Finite(y) && Finite(z) &&
            Math.Abs(x) <= MapExtent && Math.Abs(z) <= MapExtent && Math.Abs(y) <= HeightExtent;

        /// <summary>A world position: at least 3 floats, finite and on the map.</summary>
        public static bool InMap(float[] a) => a != null && a.Length >= 3 && InMap(a[0], a[1], a[2]);

        /// <summary>At least <paramref name="length" /> finite floats of sane size.</summary>
        public static bool Vector(float[] a, int length)
        {
            if (a == null || a.Length < length)
            {
                return false;
            }

            for (int i = 0; i < length; i++)
            {
                if (!Small(a[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Null, or a vector as in <see cref="Vector" />.</summary>
        public static bool OptionalVector(float[] a, int length) => a == null || Vector(a, length);

        /// <summary>Every float finite, of sane size and, for each triple at <paramref name="stride" />, on the map.</summary>
        public static bool Points(float[] a, int stride, int maxPoints)
        {
            if (a == null || a.Length % stride != 0 || a.Length / stride > maxPoints)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i += stride)
            {
                if (!InMap(a[i], a[i + 1], a[i + 2]))
                {
                    return false;
                }

                for (int j = 3; j < stride; j++)
                {
                    if (!Small(a[i + j]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>A Bezier curve (12 floats), every control point on the map.</summary>
        public static bool Curve(float[] c) =>
            c != null && c.Length >= 12 && InMap(c[0], c[1], c[2]) && InMap(c[3], c[4], c[5]) &&
            InMap(c[6], c[7], c[8]) && InMap(c[9], c[10], c[11]);

        // ---------- strings ----------

        /// <summary>Null/empty, or a prefab name ("Type/name") of sane length.</summary>
        public static bool PrefabName(string name) => name == null || name.Length <= MaxPrefabNameLength;

        /// <summary>
        ///     A name shown to players: control and format characters (bidi overrides, zero-width joiners...)
        ///     and lone surrogates removed, whitespace collapsed, at most <paramref name="max" /> characters.
        /// </summary>
        public static string CleanName(string text, int max = MaxNameLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? "";
            }

            // don't scan megabytes for a 64 character result
            int scan = Math.Min(text.Length, max * 8);
            var sb = new StringBuilder(Math.Min(scan, max));
            bool space = false;
            for (int i = 0; i < scan && sb.Length < max; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < scan && char.IsLowSurrogate(text[i + 1]) && sb.Length + 2 <= max)
                    {
                        sb.Append(c).Append(text[i + 1]);
                        space = false;
                    }

                    i++;
                    continue;
                }

                UnicodeCategory category = char.GetUnicodeCategory(c);
                if (category == UnicodeCategory.Control || category == UnicodeCategory.Format ||
                    category == UnicodeCategory.Surrogate || category == UnicodeCategory.PrivateUse ||
                    category == UnicodeCategory.OtherNotAssigned)
                {
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (!space && sb.Length > 0)
                    {
                        sb.Append(' ');
                    }

                    space = true;
                    continue;
                }

                sb.Append(c);
                space = false;
            }

            return sb.ToString().Trim();
        }

        // ---------- arrays ----------

        /// <summary>
        ///     The array, cut to <paramref name="max" /> entries (logged) and without the null entries and those
        ///     <paramref name="valid" /> refuses.
        /// </summary>
        public static T[] Filter<T>(T[] items, int max, Func<T, bool> valid, string what) where T : class
        {
            if (items == null)
            {
                return null;
            }

            if (items.Length > max)
            {
                Warn("cap:" + what, $"{what}: {items.Length} received, only the first {max} are used");
            }

            int count = Math.Min(items.Length, max);
            List<T> kept = null;
            for (int i = 0; i < count; i++)
            {
                bool ok = items[i] != null && valid(items[i]);
                if (!ok && kept == null)
                {
                    kept = new List<T>(count);
                    for (int j = 0; j < i; j++)
                    {
                        kept.Add(items[j]);
                    }
                }

                if (ok && kept != null)
                {
                    kept.Add(items[i]);
                }
            }

            if (kept != null)
            {
                Warn("bad:" + what, $"{what}: {count - kept.Count} invalid entries dropped");
                return kept.ToArray();
            }

            if (count == items.Length)
            {
                return items;
            }

            var cut = new T[count];
            Array.Copy(items, cut, count);
            return cut;
        }

        /// <summary>The array cut to <paramref name="max" /> entries (logged).</summary>
        public static T[] Cap<T>(T[] items, int max, string what)
        {
            if (items == null || items.Length <= max)
            {
                return items;
            }

            Warn("cap:" + what, $"{what}: {items.Length} received, only the first {max} are used");
            var cut = new T[max];
            Array.Copy(items, cut, max);
            return cut;
        }

        /// <summary>
        ///     A reference to a local entity: kind 0 (none), 1 (junction at A), 2 (segment from A to B) or
        ///     3 (object Prefab at A), with positions on the map. Null counts as valid (none).
        /// </summary>
        public static bool Ref(NetRef r)
        {
            if (r == null || r.Kind == 0)
            {
                return true;
            }

            switch (r.Kind)
            {
                case 1:
                    return InMap(r.A);
                case 2:
                    return InMap(r.A) && InMap(r.B);
                case 3:
                    return InMap(r.A) && !string.IsNullOrEmpty(r.Prefab) && PrefabName(r.Prefab);
                default:
                    return false;
            }
        }

        /// <summary>A reference of this kind (or none).</summary>
        public static bool Ref(NetRef r, int kind) => r == null || r.Kind == 0 || (r.Kind == kind && Ref(r));

        /// <summary>
        ///     A policy adjustment within the policy's slider range (PolicySliderData), as its panel allows.
        /// </summary>
        public static float PolicyAdjustment(Unity.Entities.EntityManager em, Unity.Entities.Entity policy, float value)
        {
            if (!Finite(value))
            {
                value = 0f;
            }

            if (policy == Unity.Entities.Entity.Null || !em.HasComponent<Game.Prefabs.PolicySliderData>(policy))
            {
                return value;
            }

            Colossal.Mathematics.Bounds1 range = em.GetComponentData<Game.Prefabs.PolicySliderData>(policy).m_Range;
            return Math.Max(range.min, Math.Min(range.max, value));
        }
    }

    /// <summary>
    ///     A queue of received commands, filled by the network handler and drained by a game system.
    ///     <list type="bullet">
    ///         <item>Bounded: past <c>capacity</c> the oldest entries are dropped (with a warning).</item>
    ///         <item>
    ///             Budgeted: <see cref="TryDequeue" /> hands out at most <c>perFrame</c> items per rendered frame,
    ///             so a flood (or everything received while the city loaded) is spread over several frames
    ///             instead of freezing one.
    ///         </item>
    ///     </list>
    ///     TryDequeue is for the main thread (it reads the frame count); Enqueue is thread-safe.
    /// </summary>
    public sealed class InboundQueue<T> where T : class
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly string _name;
        private readonly int _capacity;
        private readonly int _perFrame;
        private int _count;
        private int _frame = -1;
        private int _taken;

        public InboundQueue(string name, int capacity = 4096, int perFrame = 64)
        {
            _name = name;
            _capacity = Math.Max(1, capacity);
            _perFrame = Math.Max(1, perFrame);
        }

        public bool IsEmpty => _queue.IsEmpty;

        public int Count => Volatile.Read(ref _count);

        public void Enqueue(T item)
        {
            if (item == null)
            {
                return;
            }

            _queue.Enqueue(item);
            int dropped = 0;
            if (Interlocked.Increment(ref _count) > _capacity)
            {
                while (Volatile.Read(ref _count) > _capacity && _queue.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref _count);
                    dropped++;
                }
            }

            if (dropped > 0)
            {
                Inbound.Warn("queue:" + _name, $"{_name}: more than {_capacity} received commands waiting, oldest dropped");
            }
        }

        /// <summary>
        ///     The next item, unless this frame's budget is used up (then false, and the rest waits for the next frame).
        /// </summary>
        public bool TryDequeue(out T item)
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame != _frame)
            {
                _frame = frame;
                _taken = 0;
            }

            if (_taken >= _perFrame || !_queue.TryDequeue(out item))
            {
                item = null;
                return false;
            }

            Interlocked.Decrement(ref _count);
            _taken++;
            return true;
        }

        /// <summary>Throws away everything waiting.</summary>
        public void Clear()
        {
            while (_queue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _count);
            }
        }
    }

    /// <summary>
    ///     Base for this assembly's command handlers. Before a command is queued it checks who may send it
    ///     (<see cref="HostOnlyCommands.Accept" />) and validates it (<see cref="Validate" />, which also cuts
    ///     oversized arrays in place, so a relayed copy is cut too). Host-only commands and joiner requests are
    ///     never relayed by the host.
    /// </summary>
    public abstract class GuardedHandler<C> : CommandHandler<C> where C : CommandBase
    {
        protected GuardedHandler()
        {
            TransactionCmd = false;
            if (!HostOnlyCommands.MayRelay(typeof(C)))
            {
                RelayOnServer = false;
            }
        }

        protected sealed override void Handle(C command)
        {
            if (command == null || !HostOnlyCommands.Accept(command))
            {
                return;
            }

            bool valid;
            try
            {
                valid = Validate(command);
            }
            catch (Exception e)
            {
                Inbound.Failed(typeof(C).Name, e);
                valid = false;
            }

            if (!valid)
            {
                Inbound.Warn("invalid:" + typeof(C).Name, $"Refused an invalid {typeof(C).Name} from player {command.SenderId}");
                return;
            }

            Apply(command);
        }

        /// <summary>
        ///     Checks (and may trim) the received command. False drops it.
        /// </summary>
        protected virtual bool Validate(C command) => true;

        protected abstract void Apply(C command);
    }
}
