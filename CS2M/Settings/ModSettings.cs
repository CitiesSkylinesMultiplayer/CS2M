using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;
using System.Collections.Generic;

namespace CS2M.Settings
{

    [FileLocation(Mod.Name)]
    [SettingsUITabOrder(Section)]
    [SettingsUIGroupOrder(GeneralSettings, AdvancedSettings)]
    [SettingsUIShowGroupName(GeneralSettings, AdvancedSettings)]
    public class ModSettings : ModSetting
    {
        private const string Section = "CS2M";
        private const string GeneralSettings = "GeneralSettings";
        private const string AdvancedSettings = "AdvancedSettings";

        // The original CSM API server, which never existed for CS2; saved settings may still hold it
        private const string LegacyApiServer = "api.citiesskylinesmultiplayer.com";
        private const string DefaultApiServer = "relay.shep.rip";
        private const string DefaultApiServerPort = "4242";
        private const string DefaultLobbyUrl = "https://cs2.shep.rip";

        [SettingsUISection(Section, GeneralSettings)]
        [SettingsUIDropdown(typeof(ModSettings), nameof(GetLoggingLevels))]
        [SettingsUISetter(typeof(ModSettings), nameof(OnSetLoggingLevel))]
        public int LoggingLevel { get; set; }

        [SettingsUISection(Section, AdvancedSettings)]
        [SettingsUITextInput]
        public string ApiServer { get; set; }

        [SettingsUISection(Section, AdvancedSettings)]
        [SettingsUITextInput]
        public string ApiServerPort { get; set; }

        // Server browser list (HTTP), the lobby server behind the API server
        [SettingsUIHidden]
        public string LobbyUrl { get; set; }

        // Last used connection details, so the join and host menus remember them (upstream issue #17)
        [SettingsUIHidden]
        public string LastJoinIp { get; set; }

        [SettingsUIHidden]
        public int LastJoinPort { get; set; }

        [SettingsUIHidden]
        public int LastHostPort { get; set; }

        [SettingsUIHidden]
        public string LastUsername { get; set; }

        [SettingsUIHidden]
        public bool HudCollapsed { get; set; }

        // Addresses the host has banned, ';'-separated. Stays on this PC.
        [SettingsUIHidden]
        public string BannedAddresses { get; set; }

        // Player limit for hosted sessions, the host included; 0 is no limit
        [SettingsUIHidden]
        public int HostMaxPlayers { get; set; }

        // Sessions joined lately, newest first, as JSON (see UI/RecentSessions.cs)
        [SettingsUIHidden]
        public string RecentSessions { get; set; }

        public ModSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        public sealed override void SetDefaults()
        {
            LoggingLevel = Level.Info.severity;
            ApiServer = DefaultApiServer;
            ApiServerPort = DefaultApiServerPort;
            LobbyUrl = DefaultLobbyUrl;
            LastJoinIp = "";
            LastJoinPort = 4230;
            LastHostPort = 4230;
            LastUsername = "";
            HostMaxPlayers = 0;
            RecentSessions = "";
        }

        public static DropdownItem<int>[] GetLoggingLevels()
        {
            List<DropdownItem<int>> list = new List<DropdownItem<int>>();
            foreach (Level level in new[] { Level.Error, Level.Warn, Level.Info, Level.Debug, Level.Trace })
            {
                DropdownItem<int> dropdownItem = new DropdownItem<int>
                {
                    value = level.severity,
                    displayName = level.ToString()
                };
                list.Add(dropdownItem);
            }

            return list.ToArray();
        }

        public void OnSetLoggingLevel(int level)
        {
            Log.SetLoggingLevel(Level.GetLevel(level));
        }

        /// <summary>
        ///     Moves saved settings off values that no longer work. Returns true if anything changed.
        /// </summary>
        public bool Migrate()
        {
            bool changed = false;
            if (string.Equals(ApiServer?.Trim(), LegacyApiServer, System.StringComparison.OrdinalIgnoreCase))
            {
                ApiServer = DefaultApiServer;
                ApiServerPort = DefaultApiServerPort;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(LobbyUrl))
            {
                LobbyUrl = DefaultLobbyUrl;
                changed = true;
            }

            return changed;
        }

        public int GetApiServerPort()
        {
            return int.TryParse(ApiServerPort, out int port) && port > 0 && port <= 65535 ? port : int.Parse(DefaultApiServerPort);
        }
    }
}
