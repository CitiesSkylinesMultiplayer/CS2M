namespace CS2M.Commands.ApiServer
{
    /// <summary>
    ///     Request the global server to check the given port for connectivity.
    /// </summary>
    /// Sent by:
    /// - Server
    public class PortCheckRequestCommand : ApiCommandBase
    {
        /// <summary>
        ///     The port to check.
        /// </summary>
        public int Port { get; set; }
    }
}
