using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     Where a player's mouse points on the map, so other players can see their cursor.
    ///     Sent by: CursorSystem, about 10 times a second while the cursor moves.
    /// </summary>
    public class CursorCommand : CommandBase
    {
        public string Username { get; set; }

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        /// <summary>
        ///     What the player is doing ("zoning", "roads", ...), or empty.
        /// </summary>
        public string Activity { get; set; }

        /// <summary>
        ///     False when the mouse is over the UI or off the map.
        /// </summary>
        public bool OnMap { get; set; }
    }
}
