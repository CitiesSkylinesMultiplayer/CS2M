using CS2M.API.Commands;

namespace CS2M.Commands.Data.Internal
{
    /// <summary>
    ///     Joiner to host: send me your current city again (the "Resync" button).
    /// </summary>
    /// <remarks>
    ///     Sent by: NetworkInterface.Resync
    /// </remarks>
    public class ResyncRequestCommand : CommandBase
    {
        /// <summary>
        ///     The joiner's username, which the host uses to find its connection (usernames are unique per server).
        /// </summary>
        public string Username { get; set; }
    }
}
