using Colossal.IO.AssetDatabase;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.UI;
using Game;
using Game.Modding;

namespace CS2M
{
    public class Mod : IMod
    {
        public static Settings.Settings ModSettings;

        public void OnLoad(UpdateSystem updateSystem)
        {
            CommandInternal.Instance = new CommandInternal();
            ApiCommand.Instance = new ApiCommand();

            ModSettings = new Settings.Settings(this);
            ModSettings.RegisterInOptionsUI();
            AssetDatabase.global.LoadSettings(nameof(CS2M), ModSettings, new Settings.Settings(this));
            
            updateSystem.UpdateBefore<NetworkingSystem>(SystemUpdatePhase.PreSimulation);

            ModSettings.OnSetLoggingLevel(ModSettings.LoggingLevel);

            ModSupport.Instance.Init();

            // Set up systems
            updateSystem.UpdateAt<UISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            ModSupport.Instance.DestroyConnections();

            if (ModSettings != null)
            {
                ModSettings.UnregisterInOptionsUI();
                ModSettings = null;
            }
        }
    }
}
