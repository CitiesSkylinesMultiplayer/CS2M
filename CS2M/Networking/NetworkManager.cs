using System;
using System.Net;
using System.Net.Sockets;
using CS2M.API.Commands;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using LiteNetLib;
using Timer = System.Timers.Timer;

namespace CS2M.Networking
{
    public class NetworkManager
    {
        private readonly NetManager _netManager;
        private readonly ApiServer _apiServer;
        private ConnectionConfig _connectionConfig;

        public event OnNatHolePunchSuccessful NatHolePunchSuccessfulEvent;
        public event OnNatHolePunchFailed NatHolePunchFailedEvent;

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
        }

        public bool InitConnect(ConnectionConfig connectionConfig)
        {
            if (connectionConfig.IsTokenBased())
            {
                Log.Info($"Attempting to connect to server {connectionConfig.Token}...");
            }
            else
            {
                Log.Info(
                    $"Attempting to connect to server at {connectionConfig.HostAddress}:{connectionConfig.Port}...");
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
            IPEndPoint directConnectEndpoint = null;
            if (!_connectionConfig.IsTokenBased())
            {
                // Given string to IP address (resolves domain names).
                try
                {
                    directConnectEndpoint = new IPEndPoint(NetUtils.ResolveAddress(_connectionConfig.HostAddress),
                        _connectionConfig.Port);
                }
                catch
                {
                    //ConnectionMessage = "Invalid server IP";
                    return false;
                }
            }


            EventBasedNatPunchListener natPunchListener = new EventBasedNatPunchListener();
            Timer timeout = new Timer();
            timeout.Interval = 5000;
            timeout.AutoReset = false;
            timeout.Elapsed += (sender, args) =>
            {
                NatHolePunchFailedEvent?.Invoke(directConnectEndpoint);
            };

            // Callback on for each possible IP address to connect to the server.
            // Can potentially be called multiple times (local and public IP address).
            natPunchListener.NatIntroductionSuccess += (point, type, token) =>
            {
                bool? eventResult = NatHolePunchSuccessfulEvent?.Invoke(point);
                if (eventResult != null && eventResult.Value)
                {
                    timeout.Enabled = false;
                }
            };

            string connect = "";
            if (_connectionConfig.IsTokenBased())
            {
                connect = "token:" + _connectionConfig.Token;
            }
            else if (directConnectEndpoint != null)
            {
                connect = "ip:" + directConnectEndpoint.Address;
            }

            // Register listener and send request to global server
            _netManager.NatPunchModule.Init(natPunchListener);
            try
            {
                _netManager.NatPunchModule.SendNatIntroduceRequest(
                    new IPEndPoint(IpAddress.GetIpv4(Mod.ModSettings.ApiServer), Mod.ModSettings.GetApiServerPort()), connect);
            }
            catch (Exception e)
            {
                Log.Warn(
                    $"Could not send NAT introduction request to API server at {Mod.ModSettings.ApiServer}:{Mod.ModSettings.ApiServerPort}: {e}");
            }

            return true;
        }

        private void ListenerOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel,
            DeliveryMethod deliveryMethod)
        {
        }

        private void ListenerOnPeerConnectedEvent(NetPeer peer)
        {
        }

        private void ListenerOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
        }

        private void ListenerOnNetworkErrorEvent(IPEndPoint endpoint, SocketError socketError)
        {
            string source = endpoint != null ? $"{endpoint.Address}:{endpoint.Port}" : "<Unconnected>";
            Log.Error($"Received an error from {source}. Code: {socketError}");
        }

        private void ListenerOnNetworkLatencyUpdateEvent(NetPeer peer, int latency)
        {
        }

        public delegate bool OnNatHolePunchSuccessful(IPEndPoint endpoint);

        public delegate bool OnNatHolePunchFailed(IPEndPoint endpoint);

        public void ProcessEvents()
        {
            // Poll for new events
            _netManager.NatPunchModule.PollEvents();
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
        }
    }
}
