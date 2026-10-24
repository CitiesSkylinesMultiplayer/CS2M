namespace CS2M.Commands.ApiServer
{
    /// <summary>
    ///     Removes the game server from the API server's list when the host stops it.
    /// </summary>
    /// Sent by:
    /// - Server
    public class ServerUnregisterCommand : ApiCommandBase
    {
        /// <summary>
        ///     The server token to remove.
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        ///     Proves to the lobby that this host owns the token, so nobody else can refresh or delist the
        ///     session. Sorts after Token, so no existing property key moves. Never logged.
        /// </summary>
        public string TokenSecret { get; set; }
    }
}
