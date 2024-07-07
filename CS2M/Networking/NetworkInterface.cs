using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands.ApiServer;

namespace CS2M.Networking
{
    public class NetworkInterface
    {
        private static NetworkInterface _instance;

        public static NetworkInterface Instance => _instance ?? (_instance = new NetworkInterface());

        private LocalPlayer _localPlayer;

        public void OnUpdate()
        {
            _localPlayer.OnUpdate();
        }

        public void SendToAll(CommandBase message)
        {
            _localPlayer.SendToAll(message);
        }

        public void SendToClient(Player player, CommandBase message)
        {
            if (player is RemotePlayer remotePlayer)
            {
                _localPlayer.SendToClient(remotePlayer.NetPeer, message);
            }
            else
            {
                Log.Warn("Trying to send packet to non-csm player, ignoring.");
            }
        }

        public void SendToServer(CommandBase message)
        {
            _localPlayer.SendToServer(message);
        }

        public void SendToApiServer(ApiCommandBase message)
        {
            _localPlayer.SendToApiServer(message);
        }

        public void SendToClients(CommandBase message)
        {
            _localPlayer.SendToClients(message);
        }
    }
}