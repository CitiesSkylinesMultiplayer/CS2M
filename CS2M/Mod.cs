using Colossal.IO.AssetDatabase;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.Settings;
using CS2M.UI;
using Game;
using Game.Modding;
using System.Reflection;

namespace CS2M
{
    /// <summary>
    /// The base mod class for instantiation by the game.
    /// </summary>
    public class Mod : IMod
    {
        /// <summary>
        /// The mod's default name.
        /// </summary>
        public const string Name = nameof(CS2M);

        /// <summary>
        /// Gets the active instance reference.
        /// </summary>
        public static Mod Instance { get; private set; }

        /// <summary>
        /// Gets the mod's active settings configuration.
        /// </summary>
        internal ModSettings Settings { get; private set; }

        /// <summary>
        /// Called by the game when the mod is loaded.
        /// </summary>
        /// <param name="updateSystem">Game update system.</param>
        public void OnLoad(UpdateSystem updateSystem)
        {
            // Set instance reference.
            Instance = this;
            Log.Info($"Loading {Name} version {Assembly.GetExecutingAssembly().GetName().Version}");

            // Register mod settings to game options UI.
            Log.Info($"Loading Mod Settings");
            Settings = new ModSettings(this);
            Settings.RegisterInOptionsUI();

            // Load saved settings.
            AssetDatabase.global.LoadSettings(Name, Settings, new ModSettings(this));
            Settings.OnSetLoggingLevel(Settings.LoggingLevel);
            Log.Info($"Configured and initialised mod settings");

            CommandInternal.Instance = new CommandInternal();
            ApiCommand.Instance = new ApiCommand();

            ModSupport.Instance.Init();

            // Set up systems
            updateSystem.UpdateBefore<NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateAt<UISystem>(SystemUpdatePhase.UIUpdate);
            Log.Info($"Loading complete");
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
