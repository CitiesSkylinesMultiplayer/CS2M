using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     A road segment as built: its prefab, curve, elevation and end junction positions.
    /// </summary>
    public class EdgeData
    {
        public string Prefab { get; set; }
        public float[] Curve { get; set; }
        public float[] Elevation { get; set; }
        public float[] StartNode { get; set; }
        public float[] EndNode { get; set; }
        public float[] StartElevation { get; set; }
        public float[] EndElevation { get; set; }
    }

    /// <summary>
    ///     The result of one road-tool click: the segments and junctions it removed and the segments it
    ///     created. A new junction on an existing road is "remove that segment, create its two halves".
    ///     Sent by: NetDiffCaptureSystem.
    /// </summary>
    public class NetDiffCommand : CommandBase
    {
        public NetRef[] DeletedEdges { get; set; }
        public NetRef[] DeletedNodes { get; set; }
        public EdgeData[] CreatedEdges { get; set; }
    }
}
