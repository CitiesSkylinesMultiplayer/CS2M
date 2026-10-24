using System.Reflection;
using Colossal.IO.AssetDatabase;
using CS2M.API.Commands;
using CS2M.BaseGame.Systems;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.Settings;
using CS2M.UI;
using Game;
using Game.Modding;
using Game.Rendering;
using Game.Tools;
using HarmonyLib;
using LiteNetLib;

namespace CS2M
{
    /// <summary>
    ///     The base mod class for instantiation by the game.
    /// </summary>
    public class Mod : IMod
    {
        private const string HarmonyPatchID = "com.citiesskylinesmultiplayer";

        /// <summary>
        ///     The mod's default name.
        /// </summary>
        public const string Name = nameof(CS2M);

        /// <summary>
        ///     Gets the active instance reference.
        /// </summary>
        public static Mod Instance { get; private set; }

        /// <summary>
        ///     Gets the mod's active settings configuration.
        /// </summary>
        internal ModSettings Settings { get; private set; }

        /// <summary>
        ///     Called by the game when the mod is loaded.
        /// </summary>
        /// <param name="updateSystem">Game update system.</param>
        public void OnLoad(UpdateSystem updateSystem)
        {
            // Set instance reference.
            Instance = this;
            Log.Info($"Loading {Name} version {Assembly.GetExecutingAssembly().GetName().Version}");

            // Register mod settings to game options UI.
            Log.Info("Loading Mod Settings");
            Settings = new ModSettings(this);
            Settings.RegisterInOptionsUI();

            // Load saved settings.
            AssetDatabase.global.LoadSettings(Name, Settings, new ModSettings(this));
            if (Settings.Migrate())
            {
                Log.Info($"Updated outdated saved settings (API server {Settings.ApiServer}:{Settings.ApiServerPort})");
                Settings.ApplyAndSave();
            }
            Settings.OnSetLoggingLevel(Settings.LoggingLevel);
            Log.Info("Configured and initialised mod settings");

            CommandInternal.Instance = new CommandInternal();
            Command.GetLocalUsername = () => NetworkInterface.Instance.LocalPlayer.Username;
            ApiCommand.Instance = new ApiCommand();

            NetDebug.Logger = new NetLogWrapper();

            ModSupport.Instance.Init();

            // Patch methods
            var harmony = new Harmony(HarmonyPatchID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            CS2M.Patches.ServiceUpkeepPatch.Apply(harmony);
            CS2M.Patches.MenuPausePatch.Apply(harmony);

            // Set up systems
            updateSystem.UpdateBefore<NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateAt<UISystem>(SystemUpdatePhase.UIUpdate);
            // Zoning: capture on the frame the zone tool applies, apply what other players send
            updateSystem.UpdateBefore<ZoneCaptureSystem, ApplyZonesSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAfter<ZoneApplySystem, NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            // Roads, bulldozing and game speed
            updateSystem.UpdateBefore<RoadCaptureSystem, ApplyNetSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAfter<RoadApplySystem, NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateBefore<BulldozeCaptureSystem, ApplyNetSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAfter<BulldozeApplySystem, NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateAfter<SpeedSyncSystem, NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            // The host decides building spawns, level-ups, abandonment and money; joiners copy it
            updateSystem.UpdateAfter<HostAuthoritySystem, NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateBefore<SpawnCaptureSystem, GenerateObjectsSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateBefore<ChargeCaptureSystem, ToolApplySystem>(SystemUpdatePhase.ApplyTool);
            // Roads as applied: what the click created and removed, then the zoning of the new blocks
            updateSystem.UpdateBefore<NetApplyMarkerSystem, ApplyNetSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAfter<NetDiffCaptureSystem, ToolOutputBarrier>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAfter<NetDiffApplySystem, NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            // Buildings and props placed with the object tool; taxes, budgets and the host's population
            updateSystem.UpdateBefore<PlaceCaptureSystem, ApplyObjectsSystem>(SystemUpdatePhase.ApplyTool);
            // Buildings finished by the area tool (drawn lots, e.g. landfill): sent from the applied result
            updateSystem.UpdateBefore<LotBuildingMarkerSystem, ApplyObjectsSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAfter<LotBuildingCaptureSystem, ToolOutputBarrier>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateBefore<LotBuildingCaptureSystem, AreaCaptureSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAfter<PlaceApplySystem, NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateAfter<EconomySyncSystem, NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateBefore<PopulationDisplaySystem, Game.UI.InGame.ToolbarBottomUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<ColorSyncSystem, NetworkingSystem>(SystemUpdatePhase.PreSimulation);
            // The host's budget figures, written in before the budget panel and the top bar's money trend read them
            updateSystem.UpdateBefore<BudgetFiguresSystem, Game.UI.InGame.BudgetUISystem>(SystemUpdatePhase.UIUpdate);
            // Other players' cursors: share ours, draw theirs as rings with name labels
            updateSystem.UpdateAfter<CursorSystem, ToolRaycastSystem>(SystemUpdatePhase.Raycast);
            updateSystem.UpdateBefore<CursorOverlaySystem, OverlayRenderSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<CursorUISystem>(SystemUpdatePhase.UIUpdate);
            // Each feature registers its own systems
            CS2M.BaseGame.Features.RoadUpgradesFeature.Register(updateSystem);
            CS2M.BaseGame.Features.AreasPoliciesFeature.Register(updateSystem);
            CS2M.BaseGame.Features.TransitLinesFeature.Register(updateSystem);
            CS2M.BaseGame.Features.TerrainWaterFeature.Register(updateSystem);
            CS2M.BaseGame.Features.BuildingExtrasFeature.Register(updateSystem);
            CS2M.BaseGame.Features.ProgressionSessionFeature.Register(updateSystem);
            Log.Info("Loading complete");
        }

        public void OnDispose()
        {
            new Harmony(HarmonyPatchID).UnpatchAll(HarmonyPatchID);

            ModSupport.Instance.DestroyConnections();

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
