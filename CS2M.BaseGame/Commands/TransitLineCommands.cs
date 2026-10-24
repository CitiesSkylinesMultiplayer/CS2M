using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     A transport line as it is right now: its prefab and the positions of its waypoints in order
    ///     (x, y, z per waypoint). Lines have no position of their own, so this is how every game finds the same one.
    /// </summary>
    public class LineRef
    {
        public string Prefab { get; set; }
        public float[] Waypoints { get; set; }
    }

    /// <summary>
    ///     One waypoint of a line: where it is and the stop (or other object) it connects to, if any.
    /// </summary>
    public class LineWaypointData
    {
        public float[] Position { get; set; }
        public NetRef Connection { get; set; }
    }

    /// <summary>
    ///     A line made or reshaped with the route tool. Original is null for a new line.
    ///     Sent by: TransitLineCaptureSystem.
    /// </summary>
    public class TransitLineCommand : CommandBase
    {
        public string Prefab { get; set; }
        public LineRef Original { get; set; }
        public LineWaypointData[] Waypoints { get; set; }

        /// <summary>
        ///     The building a line belongs to when it was drawn from that building's menu (usually none).
        /// </summary>
        public NetRef Owner { get; set; }

        /// <summary>
        ///     RGBA; only used for a new line (later colour changes go through TransitLineSettingsCommand).
        /// </summary>
        public uint Color { get; set; }
    }

    /// <summary>
    ///     A line removed with the route tool, the line list or the line's info panel.
    ///     Sent by: TransitLineDeleteCaptureSystem.
    /// </summary>
    public class TransitLineDeleteCommand : CommandBase
    {
        public LineRef Line { get; set; }
    }

    /// <summary>
    ///     A policy on a line (active/inactive, day/night, ticket price, vehicle count are all policies).
    /// </summary>
    public class LinePolicyData
    {
        public string Policy { get; set; }
        public bool Active { get; set; }
        public float Adjustment { get; set; }
    }

    /// <summary>
    ///     Everything a player can set on a line from the UI. Sent by: TransitLineSettingsSystem.
    /// </summary>
    public class TransitLineSettingsCommand : CommandBase
    {
        public LineRef Line { get; set; }
        public uint Color { get; set; }
        public string Name { get; set; }
        public int Number { get; set; }
        public LinePolicyData[] Policies { get; set; }
    }
}
