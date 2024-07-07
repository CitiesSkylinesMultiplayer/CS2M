
namespace CS2M.Commands.ApiServer
{
    /// <summary>
    ///     Registers the game server on the API server to enable NAT hole punching.
    /// </summary>
    /// Sent by:
    /// - Server
    public class ServerRegistrationCommand : ApiCommandBase
    {
        /// <summary>
        ///     The server token to register.
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        ///     The server's IP address in the local network.
        /// </summary>
        public string LocalIp { get; set; }

        /// <summary>
        ///     The configured local port.
        /// </summary>
        public int LocalPort { get; set; }
    }
}
