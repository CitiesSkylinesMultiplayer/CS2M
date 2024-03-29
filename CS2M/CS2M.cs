using Colossal.IO.AssetDatabase;
using CS2M.Commands;
using CS2M.Mods;
using CS2M.Settings;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace CS2M
{
    public class CS2M : IMod
    {
        public static Settings.Settings Settings;

        public void OnLoad(UpdateSystem updateSystem)
        {
            CommandInternal.Instance = new CommandInternal();

            Settings = new Settings.Settings(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new SettingsEn(Settings));
            GameManager.instance.localizationManager.AddSource("de-DE", new SettingsDe(Settings));
            AssetDatabase.global.LoadSettings(nameof(CS2M), Settings, new Settings.Settings(this));

            Settings.OnSetLoggingLevel(Settings.LoggingLevel);

            ModSupport.Instance.Init();
        }

        public void OnDispose()
        {
            ModSupport.Instance.DestroyConnections();

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
