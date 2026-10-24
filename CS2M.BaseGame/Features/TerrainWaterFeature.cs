using CS2M.BaseGame.Systems;
using Game;
using Game.Tools;

namespace CS2M.BaseGame.Features
{
    /// <summary>
    ///     Terraforming (terrain tool brush stamps) and water sources (water tool).
    /// </summary>
    public static class TerrainWaterFeature
    {
        public static void Register(UpdateSystem updateSystem)
        {
            updateSystem.UpdateBefore<TerrainBrushCaptureSystem, ApplyBrushesSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAt<TerrainBrushSendSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAfter<TerrainBrushApplySystem, HostAuthoritySystem>(SystemUpdatePhase.PreSimulation);

            updateSystem.UpdateBefore<WaterSourceCaptureSystem, ApplyWaterSourcesSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAfter<WaterSourceApplySystem, HostAuthoritySystem>(SystemUpdatePhase.PreSimulation);
        }
    }
}
