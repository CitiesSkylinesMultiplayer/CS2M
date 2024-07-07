using Colossal.IO.AssetDatabase;
using CS2M.Commands;
using CS2M.Mods;
using CS2M.UI;
using Game;
using Game.Modding;

namespace CS2M
{
    public class Mod : IMod
    {
        public static Settings.Settings Settings;

        public void OnLoad(UpdateSystem updateSystem)
        {
            CommandInternal.Instance = new CommandInternal();

            Settings = new Settings.Settings(this);
            Settings.RegisterInOptionsUI();
            AssetDatabase.global.LoadSettings(nameof(CS2M), Settings, new Settings.Settings(this));

            Settings.OnSetLoggingLevel(Settings.LoggingLevel);

            ModSupport.Instance.Init();

            // Set up systems
            updateSystem.UpdateAt<UISystem>(SystemUpdatePhase.UIUpdate);
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
