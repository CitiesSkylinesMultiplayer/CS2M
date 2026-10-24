using CS2M.BaseGame.Systems;
using Game;
using Game.Tools;

namespace CS2M.BaseGame.Features
{
    /// <summary>
    ///     Areas drawn with the area tool (districts, surfaces, parking and extractor areas) and city/district
    ///     policies.
    /// </summary>
    public static class AreasPoliciesFeature
    {
        public static void Register(UpdateSystem updateSystem)
        {
            updateSystem.UpdateBefore<AreaApplyMarkerSystem, ApplyAreasSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAfter<AreaCaptureSystem, ToolOutputBarrier>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAfter<AreaApplySystem, HostAuthoritySystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateAfter<PolicySyncSystem, HostAuthoritySystem>(SystemUpdatePhase.PreSimulation);
            // after areas are applied, so a new district exists before its name index arrives
            updateSystem.UpdateAfter<DistrictNameSyncSystem, AreaApplySystem>(SystemUpdatePhase.PreSimulation);
        }
    }
}
