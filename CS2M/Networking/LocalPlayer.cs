using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands.ApiServer;
using LiteNetLib;
using System;

namespace CS2M.Networking
{
    public class LocalPlayer : Player
    {
        private NetworkManager _networkManager;

        public LocalPlayer() : base()
        {
        }

        public bool GetServerInfo(ConnectionConfig connectionConfig, Action<string> onStart)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                return false;
            }
            // Check if is in main menu

            _networkManager = new NetworkManager();

            _networkManager.NatHolePunchSuccessfulEvent += NatConnect;
            _networkManager.NatHolePunchFailedEvent += DirectConnect;
            _networkManager.ClientConnectSuccessfulEvent += DownloadingMap;
            _networkManager.ClientConnectFailedEvent += Inactive;
            _networkManager.ClientDisconnectEvent += Inactive;

            if (!_networkManager.InitConnect(connectionConfig))
            {
                onStart?.Invoke("= Can't init connect to server =");
                return false;
            }

            if (!_networkManager.SetupNatConnect())
            {
                onStart?.Invoke("= Can't perform NAT connection to server ");
                return false;
            }

            PlayerType = PlayerType.CLIENT;
            PlayerStatus = PlayerStatus.GET_SERVER_INFO;
            onStart?.Invoke("= Connection to server is succsesful! =");
            return true;
        }

        public bool NatConnect()
        {
            if (PlayerStatus != PlayerStatus.GET_SERVER_INFO)
            {
                return false;
            }
            
            if (!_networkManager.Connect())
            {
                Inactive();
                return false;
            }
            
            PlayerStatus = PlayerStatus.NAT_CONNECT;
            return true;
        }

        public bool DirectConnect()
        {
            if (PlayerStatus != PlayerStatus.GET_SERVER_INFO &&
                PlayerStatus != PlayerStatus.NAT_CONNECT)
            {
                return false;
            }

            if (!_networkManager.Connect())
            {
                Inactive();
                return false;
            }
            
            PlayerStatus = PlayerStatus.DIRECT_CONNECT;
            return true;
        }

        public bool DownloadingMap()
        {
            // TODO: Change, when implemented Map transfer
            return Playing();
        }

        public void LoadingMap()
        {
        }

        public bool Playing()
        {
            return true;
        }

        // INACTIVE -> PLAYING (Server)
        public bool Playing(ConnectionConfig connectionConfig, Action<string> onStart)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                return false;
            }

            _networkManager = new NetworkManager();

            bool serverStarted = _networkManager.StartServer(connectionConfig);
            if (!serverStarted)
            {
                onStart?.Invoke("= Error! The server failed to start. =");
                return false;
            }

            //TODO: Setup server variables (player list, etc.)
            PlayerType = PlayerType.SERVER;
            PlayerStatus = PlayerStatus.PLAYING;

            onStart?.Invoke("= The server has started succesfully. =");
            
            return true;
        }

        public void Blocked()
        {
        }

        // PLAYING -> INACTIVE
        public bool Inactive()
        {
            // if (PlayerStatus != PlayerStatus.PLAYING)
            // {
            //     return false;
            // }

            if (PlayerType == PlayerType.SERVER)
            {
                //TODO: Clear server variables (player list, etc.)
            } 
            else if (PlayerType == PlayerType.CLIENT)
            {
                //TODO: Clean-Up client
            }
            _networkManager.Stop();

            PlayerStatus = PlayerStatus.INACTIVE;
            return true;
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