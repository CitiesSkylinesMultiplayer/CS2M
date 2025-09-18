using CS2M.API;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Util;
using LiteNetLib;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using CS2M.Commands.Data.Internal;
using CS2M.Commands.Handler.Internal;
using Timer = System.Timers.Timer;

namespace CS2M.Networking
{
    public class NetworkManager
    {
        private const string ConnectionKey = "CSM";

        private readonly NetManager _netManager;
        private readonly ApiServer _apiServer;
        private ConnectionConfig _connectionConfig;
        private IPEndPoint _connectEndpoint;
        private Timer _timeout;
        private bool _pollNatEvent = false;

        public event OnNatHolePunchSuccessful NatHolePunchSuccessfulEvent;
        public event OnNatHolePunchFailed NatHolePunchFailedEvent;
        public event OnClientConnectSuccessful ClientConnectSuccessfulEvent;
        public event OnClientConnectFailed ClientConnectFailedEvent;
        public event OnClientDisconnect ClientDisconnectEvent;

        public NetworkManager()
        {
            // Set up network items
            EventBasedNetListener listener = new EventBasedNetListener();
            _netManager = new NetManager(listener)
            {
                NatPunchEnabled = true,
                UnconnectedMessagesEnabled = true
            };
            _apiServer = new ApiServer(_netManager);

            // Listen to events
            listener.NetworkReceiveEvent += ListenerOnNetworkReceiveEvent;
            listener.NetworkErrorEvent += ListenerOnNetworkErrorEvent;
            listener.PeerConnectedEvent += ListenerOnPeerConnectedEvent;
            listener.PeerDisconnectedEvent += ListenerOnPeerDisconnectedEvent;
            listener.NetworkLatencyUpdateEvent += ListenerOnNetworkLatencyUpdateEvent;
            listener.ConnectionRequestEvent += ListenerOnConnectionRequestEvent;
        }

        public bool InitConnect(ConnectionConfig connectionConfig)
        {
            Log.Trace("NetworkManager: InitConnect");

            if (connectionConfig.IsTokenBased())
            {
                Log.Info($"Initialize connect to server {connectionConfig.Token}...");
            }
            else
            {
                Log.Info($"Initialize connect to server at {connectionConfig.HostAddress}:{connectionConfig.Port}...");
            }

            _connectionConfig = connectionConfig;

            bool result = _netManager.Start();
            if (!result)
            {
                Log.Error("The client failed to start.");
                //ConnectionMessage = "Client failed to start.";
                return false;
            }

            return true;
        }

        public bool SetupNatConnect()
        {
            Log.Trace("NetworkManager: Setting up NAT connect");

            IPEndPoint directEndpoint = null;
            if (!_connectionConfig.IsTokenBased())
            {
                // Given string to IP address (resolves domain names).
                try
                {
                    directEndpoint = IPUtil.CreateIPEndPoint(_connectionConfig.HostAddress, _connectionConfig.Port);
                }
                catch
                {
                    //ConnectionMessage = "Invalid server IP";
                    return false;
                }
            }

            _pollNatEvent = true;

            EventBasedNatPunchListener natPunchListener = new EventBasedNatPunchListener();
            _timeout = new Timer
            {
                Interval = 5000,
                AutoReset = false
            };
            _timeout.Elapsed += (sender, args) =>
            {
                Log.Debug("NAT hole punch failed, trying direct connect");
                _pollNatEvent = false;
                _connectEndpoint = directEndpoint;
                NatHolePunchFailedEvent?.Invoke();
            };

            // Callback on for each possible IP address to connect to the server.
            // Can potentially be called multiple times (local and public IP address).
            natPunchListener.NatIntroductionSuccess += (point, type, token) =>
            {
                Log.Debug($"NAT hole punch successful ({point.Address}:{point.Port})");
                _pollNatEvent = false;
                _connectEndpoint = point;
                bool? eventResult = NatHolePunchSuccessfulEvent?.Invoke();
                if (eventResult != null && eventResult.Value)
                {
                    _timeout.Enabled = false;
                }
            };

            string connect = "";
            if (_connectionConfig.IsTokenBased())
            {
                connect = "token:" + _connectionConfig.Token;
            }
            else if (directEndpoint != null)
            {
                connect = "ip:" + directEndpoint.Address;
            }

            // Register listener and send request to global server
            _netManager.NatPunchModule.Init(natPunchListener);
            try
            {
                Log.Trace("NetworkManager: Start NAT hole punch");
                _netManager.NatPunchModule.SendNatIntroduceRequest(
                    IPUtil.CreateIP4EndPoint(Mod.Instance.Settings.ApiServer, Mod.Instance.Settings.GetApiServerPort()),
                    connect);
                _timeout.Start();
            }
            catch (Exception e)
            {
                Log.Warn(
                    $"Could not send NAT introduction request to API server at {Mod.Instance.Settings.ApiServer}:{Mod.Instance.Settings.ApiServerPort}: {e}");
            }

            return true;
        }

        public bool Connect()
        {
            if (_connectEndpoint == null)
            {
                Log.Error("No valid endpoint to connect to.");
                return false;
            }

            Log.Debug($"Connect to {_connectEndpoint.Address}:{_connectEndpoint.Port}");

            try
            {
                _timeout = new Timer
                {
                    Interval = 5000,
                    AutoReset = false
                };
                _timeout.Elapsed += (sender, args) =>
                {
                    Log.Debug($"Connect to client ({_connectEndpoint.Address}:{_connectEndpoint.Port}) failed");
                    ClientConnectFailedEvent?.Invoke();
                };

                _netManager.Connect(_connectEndpoint, ConnectionKey);
                _timeout.Start();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to connect to {_connectEndpoint.Address}:{_connectEndpoint.Port}", ex);
                return false;
            }

            return true;
        }

        public string GetConnectionPassword()
        {
            return _connectionConfig.Password;
        }

        private void ListenerOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            CommandBase command = CommandInternal.Instance.Deserialize(reader.GetRemainingBytes());
            CommandHandler handler = CommandInternal.Instance.GetCommandHandler(command.GetType());
            Log.Trace($"NetworkManager: OnNetworkReceiveEvent [PeerId: {peer.Id}] {command.GetType()}");
            if (command is PreconditionsCheckCommand)
            {
                ((PreconditionsCheckHandler) handler).HandleOnServer((PreconditionsCheckCommand) command, peer);
                return;
            }

            if (NetworkInterface.Instance.LocalPlayer.PlayerType == PlayerType.SERVER &&
                !NetworkInterface.Instance.IsPeerConnected(peer))
            {
                return;
            }
            
            //TODO: Check that only the relevant command could be sent in connected, not joined state

            handler.Parse(command);
        }

        private void ListenerOnPeerConnectedEvent(NetPeer peer)
        {
            Log.Trace($"NetworkManager: OnPeerConnectedEvent [PeerId: {peer.Id}]");
            if (NetworkInterface.Instance.LocalPlayer.PlayerType == PlayerType.CLIENT)
            {
                _timeout.Enabled = false;
                ClientConnectSuccessfulEvent?.Invoke();
            }
            else if (NetworkInterface.Instance.LocalPlayer.PlayerType == PlayerType.SERVER)
            {
                _timeout = new Timer
                {
                    Interval = 5000,
                    AutoReset = false
                };
                _timeout.Elapsed += (sender, args) =>
                {
                    if (NetworkInterface.Instance.GetPlayerByPeer(peer) == null)
                    {
                        Log.Warn($"Client peer {peer.Id} did not register within {_timeout.Interval / 1000} seconds. Disconnecting peer.");
                        peer.Disconnect();
                    }
                };
                _timeout.Start();
            }
        }

        private void ListenerOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Log.Trace($"NetworkManager: OnPeerDisconnectedEvent [PeerId: {peer.Id}]");
            if (NetworkInterface.Instance.LocalPlayer.PlayerType == PlayerType.CLIENT)
            {
                // TODO: Use disconnect info
                ClientDisconnectEvent?.Invoke();
            }
            else if (NetworkInterface.Instance.LocalPlayer.PlayerType == PlayerType.SERVER)
            {
                NetworkInterface.Instance.GetPlayerByPeer(peer)?.HandleDisconnect();
                // TODO: Handle peer disconnect on server
            }
        }

        private void ListenerOnNetworkErrorEvent(IPEndPoint endpoint, SocketError socketError)
        {
            string source = endpoint != null ? $"{endpoint.Address}:{endpoint.Port}" : "<Unconnected>";
            Log.Error($"Received an error from {source}. Code: {socketError}");
        }

        private void ListenerOnNetworkLatencyUpdateEvent(NetPeer peer, int latency)
        {
            Log.Trace($"NetworkManager: OnNetworkLatencyUpdateEvent [PeerId: {peer.Id}, Latency: {latency}]");
        }

        private void ListenerOnConnectionRequestEvent(ConnectionRequest request)
        {
            Log.Trace("NetworkManager: OnConnectionRequestEvent");
            request.AcceptIfKey(ConnectionKey);
        }

        public delegate bool OnNatHolePunchSuccessful();

        public delegate bool OnNatHolePunchFailed();

        public delegate bool OnClientConnectSuccessful();

        public delegate bool OnClientConnectFailed();

        public delegate bool OnClientDisconnect();

        public void ProcessEvents()
        {
            // Poll for new events
            if (_pollNatEvent)
            {
                _netManager.NatPunchModule.PollEvents();
            }

            _netManager.PollEvents();
            // Trigger keepalive to api server
            _apiServer.KeepAlive(_connectionConfig);
        }

        public void SendToAllClients(CommandBase message)
        {
            _netManager.SendToAll(CommandInternal.Instance.Serialize(message), DeliveryMethod.ReliableOrdered);

            Log.Debug($"Sending {message.GetType().Name} to all clients");
        }

        public void SendToClient(NetPeer peer, CommandBase message)
        {
            peer.Send(CommandInternal.Instance.Serialize(message), DeliveryMethod.ReliableOrdered);

            Log.Debug($"Sending {message.GetType().Name} to client at {peer.Address}:{peer.Port}");
        }

        public void SendToServer(CommandBase message)
        {
            NetPeer server = _netManager.ConnectedPeerList[0];
            server.Send(CommandInternal.Instance.Serialize(message), DeliveryMethod.ReliableOrdered);

            Log.Debug($"Sending {message.GetType().Name} to server");
        }

        public void SendToApiServer(ApiCommandBase message)
        {
            _apiServer.SendCommand(message);
            Log.Debug($"Sending {message.GetType().Name} to api server");
        }

        public bool StartServer(ConnectionConfig connectionConfig)
        {
            _connectionConfig = connectionConfig;

            // Let the user know that we are trying to start the server
            Log.Info($"Attempting to start server on port {_connectionConfig.Port}...");

            // Attempt to start the server
            bool result = _netManager.Start(_connectionConfig.Port);

            // If the server has not started, tell the user and return false.
            if (!result)
            {
                Log.Error("The server failed to start.");
                Stop(); // Make sure the server is fully stopped
                return false;
            }

            //TODO: NAT UPnP open Port
            //TODO: Check if port is reachable

            // Update the console to let the user know the server is running
            Log.Info("The server has started.");
            Chat.Instance.PrintGameMessage("CS2M.NetworkManager.ServerStarted".Translate());

            return true;
        }

        public void Stop()
        {
            _netManager.Stop();
            Log.Info("NetworkManager stopped.");
        }
    }
}
