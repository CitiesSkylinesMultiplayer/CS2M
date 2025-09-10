using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands.ApiServer;
using System;

namespace CS2M.Networking
{
    public class NetworkInterface
    {
        private static NetworkInterface _instance;

        public static NetworkInterface Instance => _instance ?? (_instance = new NetworkInterface());

        internal readonly LocalPlayer LocalPlayer = new LocalPlayer();

        public void OnUpdate()
        {
            LocalPlayer.OnUpdate();
        }

        public void Connect(ConnectionConfig connectionConfig)
        {
            LocalPlayer.GetServerInfo(connectionConfig);
        }

        public void StartServer(ConnectionConfig connectionConfig)
        {
            LocalPlayer.Playing(connectionConfig);
        }

        public void SendToAll(CommandBase message)
        {
            LocalPlayer.SendToAll(message);
        }

        public void SendToClient(Player player, CommandBase message)
        {
            if (player is RemotePlayer remotePlayer)
            {
                LocalPlayer.SendToClient(remotePlayer.NetPeer, message);
            }
            else
            {
                Log.Warn("Trying to send packet to non-csm player, ignoring.");
            }
        }

        public void SendToServer(CommandBase message)
        {
            LocalPlayer.SendToServer(message);
        }

        public void SendToApiServer(ApiCommandBase message)
        {
            LocalPlayer.SendToApiServer(message);
        }

        public void SendToClients(CommandBase message)
        {
            LocalPlayer.SendToClients(message);
        }
    }
}