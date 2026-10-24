using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     A node or edge in the world, described by position because entity ids differ between games.
    ///     Kind: 0 = none, 1 = node at A, 2 = edge from A to B, 3 = object (building, prop, tree) of Prefab at A.
    /// </summary>
    public class NetRef
    {
        public int Kind { get; set; }
        public string Prefab { get; set; }
        public float[] A { get; set; }
        public float[] B { get; set; }
    }

    /// <summary>
    ///     One end of a road course (Game.Tools.CoursePos).
    /// </summary>
    public class CoursePosData
    {
        public NetRef Entity { get; set; }
        public float[] Position { get; set; }
        public float[] Rotation { get; set; }
        public float[] Elevation { get; set; }
        public float CourseDelta { get; set; }
        public float SplitPosition { get; set; }
        public uint Flags { get; set; }
        public int ParentMesh { get; set; }
    }

    /// <summary>
    ///     One road definition from the road tool (CreationDefinition + NetCourse).
    /// </summary>
    public class NetCourseData
    {
        public string Prefab { get; set; }
        public uint CreationFlags { get; set; }
        public int RandomSeed { get; set; }

        /// <summary>
        ///     The existing road this definition rebuilds (e.g. the two halves of a road split by a new junction).
        /// </summary>
        public NetRef Original { get; set; }

        public float[] Curve { get; set; }
        public float[] Elevation { get; set; }
        public float Length { get; set; }
        public int FixedIndex { get; set; }
        public CoursePosData Start { get; set; }
        public CoursePosData End { get; set; }
    }

    /// <summary>
    ///     Everything the road tool built in one click, as tool definitions. Applied by RoadApplySystem; nothing sends
    ///     it at present, because roads are synced as the applied result (NetDiffCommand).
    /// </summary>
    public class RoadCommand : CommandBase
    {
        public NetCourseData[] Courses { get; set; }
    }
}
