using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Colossal.Json;
using CS2M.Mods;

namespace CS2M.UI
{
    /// <summary>
    ///     The last sessions this player joined, for the join menu's Recent tab. Kept in
    ///     <see cref="Settings.ModSettings.RecentSessions" /> as JSON:
    ///     [{"token", "ip", "port", "name", "host", "time"}], newest first. A lobby session has a token (only valid
    ///     while that server runs; the UI looks the host up again by city and host name), a direct join an ip.
    /// </summary>
    public static class RecentSessions
    {
        private const int MaxEntries = 10;

        public sealed class Entry
        {
            public string Token = "";
            public string Ip = "";
            public int Port;
            public string Name = "";
            public string Host = "";

            /// <summary>Unix seconds of the last join.</summary>
            public long Time;

            public bool SameAs(Entry other)
            {
                if (!string.IsNullOrEmpty(Ip) || !string.IsNullOrEmpty(other.Ip))
                {
                    return Ip == other.Ip && Port == other.Port;
                }

                // tokens change with every server start; the same city by the same host is the same session
                return Token == other.Token || (Name != "" && Name == other.Name && Host == other.Host);
            }
        }

        public static List<Entry> Load(string json)
        {
            var entries = new List<Entry>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return entries;
            }

            try
            {
                Variant list = JSON.Load(json);
                for (int i = 0; i < list.Count && entries.Count < MaxEntries; i++)
                {
                    Variant item = list[i];
                    var entry = new Entry
                    {
                        Token = Text(item, "token", 64),
                        Ip = Text(item, "ip", 128),
                        Port = item.TryGetValue("port", out Variant port) ? (int)port : 0,
                        Name = Text(item, "name", 64),
                        Host = Text(item, "host", 32),
                        Time = item.TryGetValue("time", out Variant time) ? (long)time : 0
                    };
                    if (entry.Token != "" || entry.Ip != "")
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Ignoring unreadable recent sessions: {e.Message}");
            }

            return entries;
        }

        /// <summary>
        ///     Puts the entry first, replacing an older one for the same session.
        /// </summary>
        public static List<Entry> Add(List<Entry> entries, Entry entry)
        {
            entry.Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var result = new List<Entry> { entry };
            result.AddRange(entries.Where(other => !other.SameAs(entry)).Take(MaxEntries - 1));
            return result;
        }

        public static string ToJson(IEnumerable<Entry> entries)
        {
            var sb = new StringBuilder("[");
            foreach (Entry entry in entries)
            {
                if (sb.Length > 1)
                {
                    sb.Append(',');
                }

                sb.Append("{\"token\":").Append(EnabledMods.JsonString(entry.Token))
                    .Append(",\"ip\":").Append(EnabledMods.JsonString(entry.Ip))
                    .Append(",\"port\":").Append(entry.Port)
                    .Append(",\"name\":").Append(EnabledMods.JsonString(entry.Name))
                    .Append(",\"host\":").Append(EnabledMods.JsonString(entry.Host))
                    .Append(",\"time\":").Append(entry.Time).Append('}');
            }

            return sb.Append(']').ToString();
        }

        private static string Text(Variant item, string key, int maxLength)
        {
            if (!item.TryGetValue(key, out Variant value) || value == null)
            {
                return "";
            }

            string text = ((string)value ?? "").Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength);
        }
    }
}
