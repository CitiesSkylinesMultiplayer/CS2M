namespace CS2M.Commands.ApiServer
{
    /// <summary>
    ///     Response of the global server to check the port
    /// </summary>
    /// Sent by:
    /// - Global Server
    public class PortCheckResultCommand : ApiCommandBase
    {
        /// <summary>
        ///     The determined state of the checked port.
        /// </summary>
        public PortCheckResult State { get; set; }

        /// <summary>
        ///     The error message in case the port check failed.
        /// </summary>
        public string Message { get; set; }
    }

    public enum PortCheckResult
    {
        Reachable,
        Unreachable,
        Error
    }
}
