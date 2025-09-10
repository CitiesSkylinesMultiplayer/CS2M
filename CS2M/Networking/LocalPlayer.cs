using Colossal.Win32;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands.ApiServer;
using CS2M.UI;
using LiteNetLib;
using System;
using static Game.Rendering.Debug.RenderPrefabRenderer;

namespace CS2M.Networking
{
    public class LocalPlayer : Player
    {

        public static LocalPlayer Instance { get; private set; }

        private NetworkManager _networkManager;
        private string _message;

        public LocalPlayer() : base()
        {
            Instance = this;
        }

        public bool GetServerInfo(ConnectionConfig connectionConfig)
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
                _message = "= Can't init connect to server =";
                printStatus(_message);
                return false;
            }

            if (!_networkManager.SetupNatConnect())
            {
                _message = "= Can't perform NAT connection to server ";
                printStatus(_message);
                return false;
            }

            PlayerType = PlayerType.CLIENT;
            PlayerStatus = PlayerStatus.GET_SERVER_INFO;

            _message = "= Connection to server is succsesful! =";
            printStatus(_message);

            return true;
        }

        private void printStatus(string message)
        {
            Log.Info(message);
            UISystem.Instance.piblishNetworkStateInUI(message);
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

            if (PlayerType == PlayerType.SERVER)
            {
                Command.CurrentRole = MultiplayerRole.Server;
            }
            else if (PlayerType == PlayerType.CLIENT)
            {
                Command.CurrentRole= MultiplayerRole.Client;
            }
            
            Command.SendToAll(new textMessageCommand($"= {Username} joined game ="));
            _networkManager.CancelConnectTimeout();
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
        public bool Playing(ConnectionConfig connectionConfig)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                return false;
            }

            _networkManager = new NetworkManager();

            bool serverStarted = _networkManager.StartServer(connectionConfig);
            if (!serverStarted)
            {
                _message = "= Error! The server failed to start. =";
                printStatus(_message);
                return false;
            }

            //TODO: Setup server variables (player list, etc.)
            PlayerType = PlayerType.SERVER;
            PlayerStatus = PlayerStatus.PLAYING;

            _message = "= The server has started succesfully. =";
            printStatus(_message);

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
                Command.GetCommandHandler(message.GetType())?.Parse(message);
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