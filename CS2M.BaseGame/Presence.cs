namespace CS2M.BaseGame
{
    /// <summary>
    ///     Whether the local player is in a menu or has the game in the background. Set by the main mod's
    ///     TimeUISystem patch (which also stops those from pausing a multiplayer session) and shown to the
    ///     other players as the cursor activity.
    /// </summary>
    public static class Presence
    {
        public static bool InMenu;
        public static bool Away;
    }
}
