using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands.Data.Internal;
using CS2M.Networking;

namespace CS2M.Commands.Handler.Internal
{
    public class ResyncRequestHandler : CommandHandler<ResyncRequestCommand>
    {
        public ResyncRequestHandler()
        {
            TransactionCmd = false;
            RelayOnServer = false;
        }

        protected override void Handle(ResyncRequestCommand command)
        {
            if (NetworkInterface.Instance.LocalPlayer.PlayerType == PlayerType.SERVER)
            {
                // SenderId was stamped by the host from the connection: a player can only resync themselves
                NetworkInterface.Instance.ResendWorld(command.SenderId);
            }
        }
    }
}
