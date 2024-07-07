using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;

namespace CS2M.Settings
{

    [FileLocation(nameof(CS2M))]
    [SettingsUITabOrder(Section)]
    [SettingsUIGroupOrder(GeneralSettings)]
    [SettingsUIShowGroupName(GeneralSettings)]
    public class Settings : ModSetting
    {
        private const string Section = "CS2M";
        private const string GeneralSettings = "GeneralSettings";

        [SettingsUISection(Section, GeneralSettings)]
        [SettingsUIDropdown(typeof(Settings), nameof(GetLoggingLevels))]
        [SettingsUISetter(typeof (Settings), nameof(OnSetLoggingLevel))]
        public int LoggingLevel { get; set; }

        public Settings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        public sealed override void SetDefaults()
        {
            LoggingLevel = Level.Debug.severity;
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
    }
}
