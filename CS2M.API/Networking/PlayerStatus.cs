namespace CS2M.API.Networking
{
    /// <summary>
    /// Various player connection states
    /// </summary>
    public enum PlayerStatus
    {
        /// <summary>
        /// The player is not currently connected to anything
        /// </summary>
        INACTIVE,
        /// <summary>
        /// The player queries the global server and attempts to get
        /// information about the server it is going to connect to
        /// </summary>
        GET_SERVER_INFO,
        /// <summary>
        /// The client is trying to connect NAT punchthrough to coonect to the server
        /// </summary>
        NAT_CONNECT,
        /// <summary>
        /// The client is trying to connect directly to the server
        /// </summary>
        DIRECT_CONNECT,
        /// <summary>
        /// The player is connected and downloading the map/save from
        /// the server
        /// </summary>
        DOWNLOADING_MAP,
        /// <summary>
        /// The player is connected, has downloaded the save game and
        /// is now loading the map
        /// </summary>
        LOADING_MAP,
        /// <summary>
        /// The player is connected to the server and currently playing
        /// the game
        /// </summary>
        PLAYING,
        /// <summary>
        /// The players is blocked from connecting to the server
        /// </summary>
        BLOCKED
    }

}