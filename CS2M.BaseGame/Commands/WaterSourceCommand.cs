using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     One water source change made with the water tool.
    /// </summary>
    public class WaterSourceChange
    {
        /// <summary>
        ///     0 = created, 1 = changed (moved, resized, height), 2 = removed.
        /// </summary>
        public int Kind { get; set; }

        /// <summary>
        ///     Where the source was before the change (changed/removed); how the receiver finds it.
        /// </summary>
        public float[] OldPosition { get; set; }

        public float[] Position { get; set; }
        public int ConstantDepth { get; set; }
        public float Radius { get; set; }
        public float Height { get; set; }
        public float Multiplier { get; set; }
        public float Polluted { get; set; }
        public float Modifier { get; set; }
    }

    /// <summary>
    ///     Water sources created, changed or removed by one apply of the water tool.
    ///     Sent by: WaterSourceCaptureSystem.
    /// </summary>
    public class WaterSourceCommand : CommandBase
    {
        public WaterSourceChange[] Changes { get; set; }
    }
}
