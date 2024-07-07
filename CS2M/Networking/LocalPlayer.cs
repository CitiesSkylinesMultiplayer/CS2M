using System.Net;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands.ApiServer;
using LiteNetLib;

namespace CS2M.Networking
{
    public class LocalPlayer : Player
    {
        private NetworkManager _networkManager;

        public LocalPlayer() : base()
        {
        }

        public bool GetServerInfo(ConnectionConfig connectionConfig)
        {
            // Check if is in main menu

            _networkManager = new NetworkManager();

            _networkManager.NatHolePunchSuccessfulEvent += NatConnect;

            if (!_networkManager.InitConnect(connectionConfig))
            {
                return false;
            }

            if (!_networkManager.SetupNatConnect())
            {
                return false;
            }

            PlayerType = PlayerType.CLIENT;
            PlayerStatus = PlayerStatus.GET_SERVER_INFO;
            return true;
        }

        public bool NatConnect(IPEndPoint endpoint)
        {
            return true;
        }

        public void DirectConnect()
        {
        }

        public void DownloadingMap()
        {
        }

        public void LoadingMap()
        {
        }

        public void Playing()
        {
        }

        public void Blocked()
        {
        }

        public void Inactive()
        {
        }

        public void OnUpdate()
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                _networkManager.ProcessEvents();
            }
        }

        public void SendToAll(CommandBase message)
        {
            message.SenderId = PlayerId;
            if (PlayerType == PlayerType.SERVER)
            {
                _networkManager.SendToAllClients(message);
            }
            else
            {
                _networkManager.SendToServer(message);
            }
        }

        public void SendToClient(NetPeer peer, CommandBase message)
        {
            message.SenderId = PlayerId;
            _networkManager.SendToClient(peer, message);
        }

        public void SendToServer(CommandBase message)
        {
            if (PlayerType == PlayerType.CLIENT)
            {
                message.SenderId = PlayerId;
                _networkManager.SendToServer(message);
            }
        }

        public void SendToClients(CommandBase message)
        {
            if (PlayerType == PlayerType.SERVER)
            {
                message.SenderId = PlayerId;
                _networkManager.SendToAllClients(message);
            }
        }

        public void SendToApiServer(ApiCommandBase message)
        {
            _networkManager.SendToApiServer(message);
        }
    }
}