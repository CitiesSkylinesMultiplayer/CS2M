namespace CS2M.Commands.ApiServer
{
    /// <summary>
    ///     Registers the game server on the API server like <see cref="ServerRegistrationCommand" />, plus what the
    ///     server browser shows about the session. Sent as the host's keep-alive.
    /// </summary>
    /// Sent by:
    /// - Server
    public class SessionInfoCommand : ApiCommandBase
    {
        /// <summary>
        ///     The server token to register.
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        ///     Proves to the lobby that this host owns the token, so nobody else can refresh or delist the
        ///     session. Sorts after Token, so no existing property key moves. Never logged.
        /// </summary>
        public string TokenSecret { get; set; }

        /// <summary>
        ///     The server's IP address in the local network.
        /// </summary>
        public string LocalIp { get; set; }

        /// <summary>
        ///     The configured local port.
        /// </summary>
        public int LocalPort { get; set; }

        /// <summary>
        ///     The name of the host's city.
        /// </summary>
        public string CityName { get; set; }

        /// <summary>
        ///     The host's username.
        /// </summary>
        public string HostName { get; set; }

        /// <summary>
        ///     Connected players, including the host.
        /// </summary>
        public int PlayerCount { get; set; }

        /// <summary>
        ///     Player limit, 0 if there is none.
        /// </summary>
        public int MaxPlayers { get; set; }

        /// <summary>
        ///     Whether joining needs a password.
        /// </summary>
        public bool HasPassword { get; set; }

        public string GameVersion { get; set; }

        public string ModVersion { get; set; }
    }
}
