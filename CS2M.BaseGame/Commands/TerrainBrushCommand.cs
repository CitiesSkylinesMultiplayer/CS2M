using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     Terrain brush stamps applied by one player's terrain tool over about 100 ms. One command holds one
    ///     tool and brush; a change of tool or brush mid-stroke starts a new command.
    ///     Sent by: TerrainBrushSendSystem (collected by TerrainBrushCaptureSystem).
    /// </summary>
    public class TerrainBrushCommand : CommandBase
    {
        /// <summary>
        ///     TerraformingPrefab ("Type/name"), decides raise/level/soften/slope and height or material.
        /// </summary>
        public string Tool { get; set; }

        /// <summary>
        ///     BrushPrefab ("Type/name"), whose texture shapes the stamp.
        /// </summary>
        public string Brush { get; set; }

        /// <summary>
        ///     StampSize floats per stamp, in application order:
        ///     position xyz, target xyz, start xyz, angle, size, strength, opacity, sender's frame delta time.
        /// </summary>
        public float[] Stamps { get; set; }

        public const int StampSize = 14;
    }
}
