using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using CS2M.Commands;
using CS2M.Mods;
using Game;
using Game.Modding;

namespace CS2M
{
    public class CS2M : IMod
    {
        public static Settings Settings;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.SetLoggingLevel(Level.Debug);
            CommandInternal.Instance = new CommandInternal();
            
            Settings = new Settings();
            AssetDatabase.global.LoadSettings(nameof(CS2M), Settings, new Settings());
            ModSupport.Instance.Init();
        }

        public void OnDispose()
        {
            ModSupport.Instance.DestroyConnections();
        }
    }
}
