using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     A tree type set on one side of a road (Game.Net.SubReplacement), with the tree prefab by name.
    /// </summary>
    public class SubReplacementData
    {
        public string Prefab { get; set; }
        public byte Type { get; set; }
        public sbyte Side { get; set; }
        public byte AgeMask { get; set; }
    }

    /// <summary>
    ///     The new state of a road segment the road tool changed in place: its prefab (replaced road type),
    ///     upgrades (raw CompositionFlags fields), direction (the junction it now starts at) and street trees.
    /// </summary>
    public class EdgeUpgradeData
    {
        public NetRef Edge { get; set; }
        public string Prefab { get; set; }
        public bool HasUpgrades { get; set; }
        public uint General { get; set; }
        public uint Left { get; set; }
        public uint Right { get; set; }
        public float[] Curve { get; set; }
        public float[] StartNode { get; set; }

        /// <summary>
        ///     Null when the segment has no Elevation component.
        /// </summary>
        public float[] Elevation { get; set; }

        public SubReplacementData[] SubReplacements { get; set; }
        public bool TramTrack { get; set; }

        /// <summary>
        ///     The Road half-alignment bits (they decide where zone cells line up), or -1 if it's not a road.
        /// </summary>
        public int RoadAlignment { get; set; }
    }

    /// <summary>
    ///     A junction the road tool changed in place: prefab and upgrades (traffic lights, roundabout, ...).
    /// </summary>
    public class NodeUpgradeData
    {
        public NetRef Node { get; set; }
        public string Prefab { get; set; }
        public bool HasUpgrades { get; set; }
        public uint General { get; set; }
        public uint Left { get; set; }
        public uint Right { get; set; }
    }

    /// <summary>
    ///     Road segments and junctions one tool click changed without replacing them (upgrades, a road
    ///     replaced by a compatible type, a one-way reversed). Sent by: RoadUpgradeCaptureSystem.
    /// </summary>
    public class RoadUpgradeCommand : CommandBase
    {
        public EdgeUpgradeData[] Edges { get; set; }
        public NodeUpgradeData[] Nodes { get; set; }
    }
}
