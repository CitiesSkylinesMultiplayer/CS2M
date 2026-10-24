using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Common;
using Game.Modding;
using Game.SceneFlow;

namespace CS2M.Mods
{
    /// <summary>
    ///     One enabled mod, as the server browser compares them between host and joiner.
    /// </summary>
    public readonly struct EnabledMod
    {
        public EnabledMod(string id, string name, string version, bool clientSide)
        {
            Id = id;
            Name = name;
            Version = version;
            ClientSide = clientSide;
        }

        /// <summary>The Paradox Mods id, or "local:" plus the assembly name for a code mod that isn't from there.</summary>
        public string Id { get; }

        public string Name { get; }

        public string Version { get; }

        /// <summary>CS2M doesn't need the other side to have it (<see cref="ModCompat" />).</summary>
        public bool ClientSide { get; }
    }

    /// <summary>
    ///     The mods this game has enabled: code mods from the game's ModManager plus everything in the active
    ///     Paradox Mods playset (asset mods, maps...). CS2M itself is left out.
    ///     The playset is read asynchronously, so call <see cref="RefreshPlayset" /> early and read later.
    /// </summary>
    public static class EnabledMods
    {
        public const string LocalPrefix = "local:";

        private static readonly object Lock = new object();
        private static List<Colossal.PSI.Common.Mod> _playset = new List<Colossal.PSI.Common.Mod>();
        private static bool _refreshing;
        private static DateTime _lastRefresh = DateTime.MinValue;

        /// <summary>
        ///     Reloads the active playset in the background, at most every few seconds. Main thread only.
        /// </summary>
        public static void RefreshPlayset(bool force = false)
        {
            lock (Lock)
            {
                if (_refreshing || (!force && DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(30)))
                {
                    return;
                }

                _refreshing = true;
                _lastRefresh = DateTime.UtcNow;
            }

            try
            {
                // Through reflection: PdxSdkPlatform lives in an assembly CS2M doesn't otherwise need,
                // and a renamed method then only costs us the asset mods instead of failing to load
                IPlatformServiceIntegration psi =
                    PlatformManager.instance?.GetPSI<IPlatformServiceIntegration>("PdxSdk");
                MethodInfo method = psi?.GetType().GetMethod("GetModsInActivePlayset", Type.EmptyTypes);
                if (method?.Invoke(psi, null) is Task<HashSet<Colossal.PSI.Common.Mod>> task)
                {
                    task.ContinueWith(done =>
                    {
                        lock (Lock)
                        {
                            _refreshing = false;
                            if (done.Status == TaskStatus.RanToCompletion && done.Result != null)
                            {
                                _playset = done.Result.ToList();
                            }
                            else if (done.Exception != null)
                            {
                                Log.Warn($"Could not read the active playset: {done.Exception.GetBaseException().Message}");
                            }
                        }
                    });
                    return;
                }

                Log.Debug("No Paradox Mods playset to read");
            }
            catch (Exception e)
            {
                Log.Warn($"Could not read the active playset: {e.Message}");
            }

            lock (Lock)
            {
                _refreshing = false;
            }
        }

        /// <summary>
        ///     The enabled mods, required ones first. Main thread only (reads the ModManager).
        /// </summary>
        public static List<EnabledMod> Get()
        {
            var mods = new List<EnabledMod>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string ownPlatformId = null;

            ModManager modManager = GameManager.instance?.modManager;
            if (modManager != null)
            {
                foreach (ModManager.ModInfo info in modManager)
                {
                    try
                    {
                        if (!info.isLoaded)
                        {
                            continue;
                        }

                        SourceMeta meta = default;
                        try
                        {
                            meta = info.asset.GetMeta();
                        }
                        catch (Exception)
                        {
                            // No meta for this asset: listed by its assembly name
                        }

                        if (info.instances.Any(instance => instance?.GetType() == typeof(Mod)))
                        {
                            ownPlatformId = meta.platformID;
                            continue;
                        }

                        // CS2M ships CS2M.API and CS2M.BaseGame next to CS2M.dll; they are part of it, not mods
                        if (info.asset.name == "CS2M.API" || info.asset.name == "CS2M.BaseGame")
                        {
                            continue;
                        }

                        string id = string.IsNullOrEmpty(meta.platformID)
                            ? LocalPrefix + info.asset.name
                            : meta.platformID;
                        if (IsCs2mDependency(id) || !ids.Add(id))
                        {
                            continue;
                        }

                        bool clientSide = info.instances.Any(instance => ModCompat.IsClientSide(instance?.GetType()));
                        string name = string.IsNullOrEmpty(meta.displayName) ? info.asset.name : meta.displayName;
                        mods.Add(new EnabledMod(id, name, info.asset.version?.ToString() ?? "", clientSide));
                    }
                    catch (Exception e)
                    {
                        Log.Debug($"Skipping a mod in the session mod list: {e.Message}");
                    }
                }
            }

            List<Colossal.PSI.Common.Mod> playset;
            lock (Lock)
            {
                playset = _playset;
            }

            foreach (Colossal.PSI.Common.Mod pdx in playset)
            {
                if (string.IsNullOrEmpty(pdx.id) || pdx.id == ownPlatformId || IsCs2mDependency(pdx.id))
                {
                    continue;
                }

                string version = !string.IsNullOrEmpty(pdx.userModVersion) ? pdx.userModVersion : pdx.version ?? "";
                int existing = mods.FindIndex(mod => mod.Id == pdx.id);
                if (existing >= 0)
                {
                    // A code mod from Paradox Mods: its published version, the same on every PC that has it
                    EnabledMod code = mods[existing];
                    mods[existing] = new EnabledMod(code.Id, string.IsNullOrEmpty(pdx.displayName) ? code.Name : pdx.displayName,
                        version, code.ClientSide);
                }
                else if (ids.Add(pdx.id))
                {
                    mods.Add(new EnabledMod(pdx.id, string.IsNullOrEmpty(pdx.displayName) ? pdx.id : pdx.displayName,
                        version, false));
                }
            }

            return mods.OrderBy(mod => mod.ClientSide)
                .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        ///     Mods every CS2M player has because CS2M needs them (Paradox Mods ids). Listing them would put
        ///     every session in the Modded tab.
        /// </summary>
        public static bool IsCs2mDependency(string platformId) => platformId == "75426"; // I18n Everywhere

        /// <summary>
        ///     The list as JSON for the UI: [{"id", "name", "version", "clientSide"}].
        /// </summary>
        public static string ToJson(IEnumerable<EnabledMod> mods)
        {
            var sb = new StringBuilder("[");
            foreach (EnabledMod mod in mods)
            {
                if (sb.Length > 1)
                {
                    sb.Append(',');
                }

                sb.Append("{\"id\":").Append(JsonString(mod.Id))
                    .Append(",\"name\":").Append(JsonString(mod.Name))
                    .Append(",\"version\":").Append(JsonString(mod.Version))
                    .Append(",\"clientSide\":").Append(mod.ClientSide ? "true" : "false").Append('}');
            }

            return sb.Append(']').ToString();
        }

        public static string JsonString(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text ?? "")
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append(' ');
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
