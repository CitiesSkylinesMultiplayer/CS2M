using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     Zoning painted on one zone block: the cells the zone tool selected and the zone they got. The block is
    ///     found by its geometry, because entity ids differ between games.
    ///     Sent by: ZoneCaptureSystem and NetDiffCaptureSystem.
    /// </summary>
    public class ZoneCellsCommand : CommandBase
    {
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float DirX { get; set; }
        public float DirY { get; set; }
        public int SizeX { get; set; }
        public int SizeY { get; set; }

        /// <summary>
        ///     Indices into the block's cell buffer.
        /// </summary>
        public int[] Cells { get; set; }

        /// <summary>
        ///     ZoneType index for each entry in Cells.
        /// </summary>
        public ushort[] Zones { get; set; }
    }
}
