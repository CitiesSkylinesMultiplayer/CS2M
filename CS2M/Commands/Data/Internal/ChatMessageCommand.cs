using CS2M.API.Commands;

namespace CS2M.Commands.Data.Internal
{
    /// <summary>
    /// Send chat messages to other players in game.
    /// </summary>
    /// <remarks>
    /// Sent by: ChatPanel
    /// </remarks>
    public class ChatMessageCommand : CommandBase
    {
        /// <summary>
        /// The username for the message sender.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// The message sent by the user.
        /// </summary>
        public string Message { get; set; }
    }
}
