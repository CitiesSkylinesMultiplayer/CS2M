using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     A building's (or other object's) custom colours from the info panel's colour picker.
    ///     Colors: 12 floats per set (rgba of channels 0, 1, 2); none with Enabled false resets to the default.
    ///     Sent by: ColorSyncSystem.
    /// </summary>
    public class ColorCommand : CommandBase
    {
        public NetRef Target { get; set; }
        public bool Enabled { get; set; }
        public float[] Colors { get; set; }
    }
}
