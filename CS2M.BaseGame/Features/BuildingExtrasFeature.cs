using CS2M.BaseGame.Systems;
using Game;
using Game.Tools;

namespace CS2M.BaseGame.Features
{
    /// <summary>
    ///     Service upgrades/extensions and moved objects reuse the placement sync (PlaceCaptureSystem/PlaceApplySystem,
    ///     with ExistingParts applying the definitions that change existing things), so they need no systems here.
    ///     This registers the info panel settings (policies, delete buttons) and the custom names.
    /// </summary>
    public static class BuildingExtrasFeature
    {
        public static void Register(UpdateSystem updateSystem)
        {
            updateSystem.UpdateBefore<BuildingSettingsSyncSystem, GenerateObjectsSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAfter<NameSyncSystem, HostAuthoritySystem>(SystemUpdatePhase.PreSimulation);
        }
    }
}
