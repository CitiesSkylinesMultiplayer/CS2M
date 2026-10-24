using CS2M.API.Commands;
using CS2M.Commands.Data.Internal;
using CS2M.Networking;

namespace CS2M.Commands.Handler.Internal
{
    /// <summary>
    ///     Host to joiner only (see CommandPolicy): the host never accepts or relays it from a joiner.
    /// </summary>
    public class WorldTransferHandler : CommandHandler<WorldTransferCommand>
    {
        public WorldTransferHandler()
        {
            RelayOnServer = false;
        }

        protected override void Handle(WorldTransferCommand command)
        {
            NetworkInterface.Instance.LocalPlayer.SliceReceived(command);
        }
    }
}
