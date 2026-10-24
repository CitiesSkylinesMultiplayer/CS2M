
namespace CS2M.API.Commands
{
    /// <summary>
    ///     A base protobuf command that all other commands in this mod should
    ///     extend. Provides support for serialization.
    ///
    ///     When creating new commands, you should create a new command ID (up to 255) which
    ///     represents this command when sending over the network.
    /// </summary>
    public abstract class CommandBase
    {
        /// <summary>
        ///     The id of the sending player: 0 for the host, 1, 2, 3... for joiners in join order (never reused
        ///     while the host runs). The host stamps it from the connection, so it can't be forged.
        /// </summary>
        public int SenderId { get; set; }
    }
}
