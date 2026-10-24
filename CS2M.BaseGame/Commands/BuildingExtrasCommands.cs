using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     A policy changed on one building or service upgrade from its info panel: out of service, emptying,
    ///     and the building's own policies and sliders (Game.Policies.Modify). Sent by: BuildingSettingsSyncSystem.
    /// </summary>
    public class BuildingPolicyCommand : CommandBase
    {
        public NetRef Target { get; set; }
        public string Policy { get; set; }
        public bool Active { get; set; }
        public float Adjustment { get; set; }
    }

    /// <summary>
    ///     A custom name set or cleared (empty Name). Target is a building/object, a road segment, or with
    ///     Street a whole named street (its first segment); City renames the city. Sent by: NameSyncSystem.
    /// </summary>
    public class RenameCommand : CommandBase
    {
        public NetRef Target { get; set; }
        public bool Street { get; set; }
        public bool City { get; set; }
        public string Name { get; set; }
    }
}
