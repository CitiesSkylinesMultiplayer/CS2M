using CS2M.BaseGame.Systems;
using Game;
using Game.Tools;

namespace CS2M.BaseGame.Features
{
    /// <summary>
    ///     Road segments and junctions changed in place by the road tool: upgrades, road type replacement,
    ///     one-way reversal and street-tree types.
    /// </summary>
    public static class RoadUpgradesFeature
    {
        public static void Register(UpdateSystem updateSystem)
        {
            updateSystem.UpdateBefore<RoadUpgradeMarkerSystem, ApplyNetSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAfter<RoadUpgradeCaptureSystem, ToolOutputBarrier>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAfter<RoadUpgradeApplySystem, HostAuthoritySystem>(SystemUpdatePhase.PreSimulation);
        }
    }
}
