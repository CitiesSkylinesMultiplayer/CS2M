using CS2M.BaseGame.Systems;
using Game;
using Game.Tools;

namespace CS2M.BaseGame.Features
{
    /// <summary>
    ///     Public transport lines: made, reshaped and removed with the route tool or the line panels, plus
    ///     their colour, name, number and policies. Vehicles on the lines are simulation and not synced.
    /// </summary>
    public static class TransitLinesFeature
    {
        public static void Register(UpdateSystem updateSystem)
        {
            updateSystem.UpdateBefore<TransitLineCaptureSystem, ApplyRoutesSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAt<TransitLineDeleteCaptureSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateAfter<TransitLineApplySystem, HostAuthoritySystem>(SystemUpdatePhase.PreSimulation);
            updateSystem.UpdateAfter<TransitLineSettingsSystem, TransitLineApplySystem>(SystemUpdatePhase.PreSimulation);
        }
    }
}
