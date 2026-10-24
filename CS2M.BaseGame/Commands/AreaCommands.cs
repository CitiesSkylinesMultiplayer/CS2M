using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     Identifies an existing area on the receiving game: its prefab and node positions (x, y, z per node).
    /// </summary>
    public class AreaKey
    {
        public string Prefab { get; set; }
        public float[] Nodes { get; set; }
    }

    /// <summary>
    ///     An area as applied: Nodes is x, y, z, elevation per node (Game.Areas.Node). Cache is the owner-relative
    ///     copy of the nodes (Game.Tools.LocalNodeCache, x, y, z per node) that areas owned by a building keep.
    /// </summary>
    public class AreaData
    {
        public string Prefab { get; set; }
        public float[] Nodes { get; set; }
        public bool Complete { get; set; }
        public NetRef Owner { get; set; }
        public float[] Cache { get; set; }
        public int[] CacheMesh { get; set; }
        public int Seed { get; set; }
    }

    public class AreaUpdate
    {
        public AreaKey Old { get; set; }
        public AreaData New { get; set; }
    }

    /// <summary>
    ///     The result of one area-tool (or bulldozer) apply: districts, surfaces, parking and extractor areas
    ///     created, reshaped or removed. Sent by: AreaCaptureSystem.
    /// </summary>
    public class AreaChangeCommand : CommandBase
    {
        public AreaKey[] Deleted { get; set; }
        public AreaUpdate[] Updated { get; set; }
        public AreaData[] Created { get; set; }
    }

    /// <summary>
    ///     One policy set from the policies panel. District is null for a city-wide policy.
    ///     Sent by: PolicySyncSystem.
    /// </summary>
    public class PolicyCommand : CommandBase
    {
        public AreaKey District { get; set; }
        public string Policy { get; set; }
        public bool Active { get; set; }
        public float Adjustment { get; set; }
    }

    /// <summary>
    ///     The random name index of each district on the host. Each game rolls its own index for a new
    ///     district, so without this the same district has a different name for every player.
    ///     Sent by: DistrictNameSyncSystem (host only).
    /// </summary>
    public class DistrictNamesCommand : CommandBase
    {
        public AreaKey[] Districts { get; set; }
        public int[] Indices { get; set; }
    }
}
