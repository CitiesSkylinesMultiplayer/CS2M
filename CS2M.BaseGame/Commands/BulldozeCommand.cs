using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     Everything one bulldozer click (or an info panel delete button) removed: junctions, roads and objects.
    ///     Sent by: BulldozeCaptureSystem and BuildingSettingsSyncSystem.
    /// </summary>
    public class BulldozeCommand : CommandBase
    {
        public NetRef[] Targets { get; set; }
    }
}
