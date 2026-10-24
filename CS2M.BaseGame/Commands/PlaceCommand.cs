using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     One tool definition entity, described without entity ids: a building or prop (Object), a road owned
    ///     by it (Course), or an area such as a parking lot (AreaNodes). Owned parts point at their owner through
    ///     OwnerPrefab/OwnerPosition/OwnerRotation, which are the same in every game.
    /// </summary>
    public class DefinitionData
    {
        public string Prefab { get; set; }
        public string SubPrefab { get; set; }
        public uint CreationFlags { get; set; }
        public int RandomSeed { get; set; }
        public NetRef Original { get; set; }

        /// <summary>
        ///     The definition changes or removes something that already exists (CreationDefinition.m_Original was set),
        ///     even when Original can't describe it (areas). Such definitions are applied by ExistingParts, never built.
        /// </summary>
        public bool HadOriginal { get; set; }

        public NetRef Owner { get; set; }

        /// <summary>
        ///     CreationDefinition.m_Attached is sometimes a prefab, sometimes a world entity.
        /// </summary>
        public string AttachedPrefab { get; set; }
        public NetRef Attached { get; set; }

        public SpawnCommand Object { get; set; }

        public bool HasOwnerDefinition { get; set; }
        public string OwnerPrefab { get; set; }
        public float[] OwnerPosition { get; set; }
        public float[] OwnerRotation { get; set; }

        public NetCourseData Course { get; set; }

        /// <summary>
        ///     Game.Areas.Node buffer: x, y, z, elevation per node.
        /// </summary>
        public float[] AreaNodes { get; set; }
    }

    /// <summary>
    ///     Everything the building/prop tool placed in one click. Sent by: PlaceCaptureSystem and LotBuildingCaptureSystem.
    /// </summary>
    public class PlaceCommand : CommandBase
    {
        public DefinitionData[] Definitions { get; set; }
    }
}
