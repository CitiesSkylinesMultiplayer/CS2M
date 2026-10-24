using CS2M.BaseGame.Systems;
using Game;
using Game.Simulation;

namespace CS2M.BaseGame.Features
{
    /// <summary>
    ///     Host-owned progression: XP, milestones, development tree, credit limit, loans and map tiles.
    ///     (Resyncing the whole city lives in the main CS2M project, next to the world transfer.)
    /// </summary>
    public static class ProgressionSessionFeature
    {
        public static void Register(UpdateSystem updateSystem)
        {
            // HostAuthoritySystem runs right after networking, so these apply what just arrived
            updateSystem.UpdateAfter<ProgressionSyncSystem, HostAuthoritySystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateAfter<MapTileSyncSystem, HostAuthoritySystem>(SystemUpdatePhase.PreSimulation);
            // joiners: drop locally earned XP after XPBuiltSystem/NetXPSystem queue it, before XPSystem adds it
            updateSystem.UpdateBefore<XPDrainSystem, XPSystem>(SystemUpdatePhase.ModificationEnd);
        }
    }
}
