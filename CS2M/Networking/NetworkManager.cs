using CS2M.API;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Util;
using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using CS2M.Commands.Data.Internal;
using CS2M.Commands.Handler.Internal;
using LiteNetLib.Utils;

namespace CS2M.Networking
{
    /// <summary>
    ///     The LiteNetLib side. Everything here runs on the main thread (events are delivered by PollEvents in
    ///     <see cref="ProcessEvents" />), so no locking is needed.
    /// </summary>
    public class NetworkManager
    {
        private const string ConnectionKey = "CSM";

        // Client: how long NAT punching and connecting may take
        private const double NatTimeoutSeconds = 5;
        private const double ConnectTimeoutSeconds = 5;

        // Host: a connection that hasn't passed the PreconditionsCheck by then is closed
        private const double RegisterTimeoutSeconds = 5;

        // Host: hard limits on connections, whatever HostMaxPlayers says
        private const int MaxPeers = 64;
        private const int MaxPendingPeers = 16;
        private const int MaxPendingPerAddress = 2;

        // Host: a peer that goes over its message rate this often is disconnected
        private const int MaxRateDrops = 1000;

        // Largest unconnected (API server) packet we read
        private const int MaxUnconnectedBytes = 8 * 1024;

        /// <summary>
        ///     Failed join attempts per address, kept between sessions so restarting the server doesn't reset them.
        /// </summary>
        private static readonly JoinGuard JoinGuard = new JoinGuard();

        private readonly NetManager _netManager;
        private readonly ApiServer _apiServer;
        private ConnectionConfig _connectionConfig;
        private IPEndPoint _connectEndpoint;
        private IPEndPoint _directEndpoint;
        private bool _pollNatEvent = false;
        private bool _stopped;

        // Client: the host connection, and the deadlines checked in ProcessEvents
        private NetPeer _serverPeer;
        private readonly Stopwatch _natWait = new Stopwatch();
        private readonly Stopwatch _connectWait = new Stopwatch();
        private Task<ResolvedEndpoints> _resolveTask;

        // Host: every accepted connection (PeerState in Tag), joining or registered
        private readonly HashSet<NetPeer> _peers = new HashSet<NetPeer>();
        private readonly Stopwatch _sinceSweep = Stopwatch.StartNew();

        public event OnNatHolePunchSuccessful NatHolePunchSuccessfulEvent;
        public event OnNatHolePunchFailed NatHolePunchFailedEvent;
        public event OnClientConnectSuccessful ClientConnectSuccessfulEvent;
        public event OnClientConnectFailed ClientConnectFailedEvent;
        public event OnClientDisconnect ClientDisconnectEvent;
        public event OnClientSetupFailed ClientSetupFailedEvent;

        public NetworkManager()
        {
            // Set up network items
            EventBasedNetListener listener = new EventBasedNetListener();
            _netManager = new NetManager(listener)
            {
                NatPunchEnabled = true,
                UnconnectedMessagesEnabled = true,
                MtuDiscovery = true,
                // Must be the same on both sides; see CommandPolicy.DeliveryOf
                ChannelsCount = CommandPolicy.ChannelsCount,
            };
            _apiServer = new ApiServer(_netManager);

            // Listen to events
            listener.NetworkReceiveEvent += ListenerOnNetworkReceiveEvent;
            listener.NetworkErrorEvent += ListenerOnNetworkErrorEvent;
            listener.PeerConnectedEvent += ListenerOnPeerConnectedEvent;
            listener.PeerDisconnectedEvent += ListenerOnPeerDisconnectedEvent;
            listener.NetworkLatencyUpdateEvent += ListenerOnNetworkLatencyUpdateEvent;
            listener.ConnectionRequestEvent += ListenerOnConnectionRequestEvent;
            listener.NetworkReceiveUnconnectedEvent += ListenerOnNetworkReceiveUnconnectedEvent;
        }

        private static bool IsServer => NetworkInterface.Instance.LocalPlayer.PlayerType == PlayerType.SERVER;

        private static bool IsClient => NetworkInterface.Instance.LocalPlayer.PlayerType == PlayerType.CLIENT;

        public bool InitConnect(ConnectionConfig connectionConfig)
        {
            Log.Trace("NetworkManager: InitConnect");

            // The address the player typed stays out of the log
            Log.Info(connectionConfig.IsTokenBased()
                ? "Initialize connect to a server from the server list..."
                : "Initialize connect to a server by address...");

            _connectionConfig = connectionConfig;

            bool result = _netManager.Start();
            if (!result)
            {
                Log.Error("The client failed to start.");
                return false;
            }

            return true;
        }

        private class ResolvedEndpoints
        {
            public IPEndPoint Direct;
            public IPEndPoint Api;
            public bool DirectFailed;
        }

        /// <summary>
        ///     Starts the NAT introduction. The name lookups (the typed host name and the API server) run in the
        ///     background, as they block; <see cref="ProcessEvents" /> carries on once they're done, or raises
        ///     <see cref="ClientSetupFailedEvent" /> when the typed address can't be resolved.
        /// </summary>
        public bool SetupNatConnect()
        {
            Log.Trace("NetworkManager: Setting up NAT connect");

            bool tokenBased = _connectionConfig.IsTokenBased();
            string host = _connectionConfig.HostAddress?.Trim();
            int port = _connectionConfig.Port;
            if (!tokenBased && string.IsNullOrEmpty(host))
            {
                return false;
            }

            string apiServer = Mod.Instance.Settings.ApiServer;
            int apiPort = Mod.Instance.Settings.GetApiServerPort();
            _resolveTask = Task.Run(() =>
            {
                var resolved = new ResolvedEndpoints();
                if (!tokenBased)
                {
                    try
                    {
                        resolved.Direct = IPUtil.CreateIPEndPoint(host, port);
                    }
                    catch
                    {
                        resolved.DirectFailed = true;
                    }
                }

                try
                {
                    resolved.Api = IPUtil.CreateIP4EndPoint(apiServer, apiPort);
                }
                catch
                {
                    // Without the API server only a direct connect is possible
                }

                return resolved;
            });

            return true;
        }

        private void ContinueNatConnect(ResolvedEndpoints resolved)
        {
            if (resolved == null || resolved.DirectFailed)
            {
                ClientSetupFailedEvent?.Invoke();
                return;
            }

            _directEndpoint = resolved.Direct;
            _pollNatEvent = true;

            EventBasedNatPunchListener natPunchListener = new EventBasedNatPunchListener();

            // Callback on for each possible IP address to connect to the server.
            // Can potentially be called multiple times (local and public IP address).
            natPunchListener.NatIntroductionSuccess += (point, type, token) =>
            {
                Log.Debug("NAT hole punch successful");
                _pollNatEvent = false;
                _connectEndpoint = point;
                bool? eventResult = NatHolePunchSuccessfulEvent?.Invoke();
                if (eventResult != null && eventResult.Value)
                {
                    _natWait.Reset();
                }
            };

            string connect = "";
            if (_connectionConfig.IsTokenBased())
            {
                connect = "token:" + _connectionConfig.Token;
            }
            else if (_directEndpoint != null)
            {
                // With the port, so the API server can tell apart two hosts behind the same address
                connect = "ip:" + _directEndpoint;
            }

            // Register listener and send request to global server
            _netManager.NatPunchModule.Init(natPunchListener);
            _natWait.Restart();
            if (resolved.Api == null)
            {
                Log.Warn($"Could not resolve the API server {Mod.Instance.Settings.ApiServer}, trying a direct connect");
                return;
            }

            try
            {
                Log.Trace("NetworkManager: Start NAT hole punch");
                _netManager.NatPunchModule.SendNatIntroduceRequest(resolved.Api, connect);
            }
            catch (Exception e)
            {
                Log.Warn(
                    $"Could not send NAT introduction request to API server at {Mod.Instance.Settings.ApiServer}:{Mod.Instance.Settings.ApiServerPort}: {e.Message}");
            }
        }

        public bool Connect()
        {
            if (_connectEndpoint == null)
            {
                Log.Warn("No valid endpoint to connect to.");
                return false;
            }

            Log.Debug("Connecting to the server");

            try
            {
                _connectWait.Restart();
                _netManager.Connect(_connectEndpoint, ConnectionKey);
            }
            catch (Exception ex)
            {
                // A warning, not an error: errors pop up in the game, and this one would show the address
                Log.Warn($"Failed to connect to the server: {ex.GetType().Name}");
                _connectWait.Reset();
                return false;
            }

            return true;
        }

        public string GetConnectionPassword()
        {
            return _connectionConfig?.Password;
        }

        private void ListenerOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel,
            DeliveryMethod deliveryMethod)
        {
            // Nothing a peer sends may throw out of PollEvents: that would skip the rest of this frame's events
            try
            {
                if (IsServer)
                {
                    ReceiveOnServer(peer, reader);
                }
                else if (IsClient)
                {
                    ReceiveOnClient(peer, reader);
                }
            }
            catch (Exception e)
            {
                PeerState state = peer.Tag as PeerState;
                string who = state?.Player != null ? $"player {state.Player.PlayerId}" : IsServer ? "a joining peer" : "the host";
                RateLimitedLog.Warn("receive:" + e.GetType().Name,
                    $"Could not handle a message from {who}: {e.GetType().Name}: {e.Message}");
                if (IsServer)
                {
                    Strike(peer, state);
                }
            }
        }

        private static ReadOnlyMemory<byte> Payload(NetPacketReader reader) =>
            new ReadOnlyMemory<byte>(reader.RawData, reader.Position, reader.AvailableBytes);

        private void ReceiveOnServer(NetPeer peer, NetPacketReader reader)
        {
            if (!(peer.Tag is PeerState state))
            {
                // Every accepted peer gets a state; anything else shouldn't be here
                peer.Disconnect();
                return;
            }

            int size = reader.AvailableBytes;
            if (!state.Registered)
            {
                // Before the join check passes, the only thing a peer may send is a small PreconditionsCheck
                if (size > CommandPolicy.MaxPendingPacketBytes)
                {
                    RateLimitedLog.Warn("pending-size", $"Disconnected a joining peer that sent {size} bytes before joining");
                    Disconnect(peer, "Invalid join request");
                    return;
                }

                if (!state.Messages.TryTake(50))
                {
                    // A dozen messages before joining is already more than a real client ever sends
                    Disconnect(peer, "Invalid join request");
                    return;
                }

                CommandBase first = CommandInternal.Instance.Deserialize(Payload(reader));
                if (first is PreconditionsCheckCommand check &&
                    CommandInternal.Instance.GetCommandHandler(typeof(PreconditionsCheckCommand)) is PreconditionsCheckHandler checkHandler)
                {
                    checkHandler.HandleOnServer(check, peer, state, JoinGuard);
                }
                else
                {
                    RateLimitedLog.Warn("pending-cmd", $"Dropped {first?.GetType().Name ?? "null"} from a peer that hasn't joined");
                    Strike(peer, state);
                }

                return;
            }

            int playerId = state.Player.PlayerId;
            if (size > CommandPolicy.MaxJoinerPacketBytes)
            {
                RateLimitedLog.Warn($"size:{playerId}", $"Dropped a {size} byte message from player {playerId}: too big");
                Strike(peer, state);
                return;
            }

            // Bigger messages cost more, so a flood of large ones runs dry sooner
            if (!state.Messages.TryTake(1 + size / 8192.0))
            {
                state.RateDrops++;
                RateLimitedLog.Warn($"rate:{playerId}", $"Player {playerId} sends too fast, dropping messages");
                if (state.RateDrops >= MaxRateDrops)
                {
                    Disconnect(peer, "You sent too many messages");
                }

                return;
            }

            CommandBase command = CommandInternal.Instance.Deserialize(Payload(reader));
            Type type = command?.GetType();
            if (!CommandPolicy.IsJoinerCommand(type))
            {
                // Host-only (world transfer, simulation results, money, join replies...) or unknown. No strike:
                // dropping it is enough, and a sync system that sends one by mistake shouldn't get players kicked.
                RateLimitedLog.Warn($"hostonly:{type?.Name}",
                    $"Dropped {type?.Name ?? "null"} from player {playerId}: only the host sends it");
                return;
            }

            if (size > CommandPolicy.MaxBytesFromJoiner(type))
            {
                RateLimitedLog.Warn($"size:{type.Name}", $"Dropped a {size} byte {type.Name} from player {playerId}: too big");
                Strike(peer, state);
                return;
            }

            CommandHandler handler = CommandInternal.Instance.GetCommandHandler(type);
            if (handler == null)
            {
                RateLimitedLog.Warn($"nohandler:{type.Name}", $"Dropped {type.Name}: no handler");
                return;
            }

            // The sender is who the connection says, never what the message says
            command.SenderId = playerId;
            if (!JoinerInput.Prepare(command, state))
            {
                return;
            }

            Log.Trace($"NetworkManager: received {type.Name} from player {playerId}");
            handler.Parse(command);

            // The host is the hub: pass player actions on to the other joiners (only once the host applied it)
            if (handler.RelayOnServer)
            {
                SendToAllClientsExcept(peer, command);
            }
        }

        private void ReceiveOnClient(NetPeer peer, NetPacketReader reader)
        {
            // A joiner only listens to the host it connected to
            if (_serverPeer == null || !ReferenceEquals(peer, _serverPeer))
            {
                return;
            }

            int size = reader.AvailableBytes;
            if (size > CommandPolicy.MaxHostPacketBytes)
            {
                RateLimitedLog.Warn("host-size", $"Dropped a {size} byte message from the host: too big");
                return;
            }

            CommandBase command = CommandInternal.Instance.Deserialize(Payload(reader));
            Type type = command?.GetType();
            CommandHandler handler = type != null ? CommandInternal.Instance.GetCommandHandler(type) : null;
            if (handler == null)
            {
                RateLimitedLog.Warn($"nohandler:{type?.Name}", $"Dropped {type?.Name ?? "null"} from the host: no handler");
                return;
            }

            // The host never relays host-only commands, so one carrying another player's id is forged
            if (CommandPolicy.IsHostOnly(type) && command.SenderId != NetworkInterface.HostPlayerId)
            {
                RateLimitedLog.Warn($"relayed:{type.Name}", $"Dropped {type.Name} relayed from player {command.SenderId}");
                return;
            }

            Log.Trace($"NetworkManager: received {type.Name} from {command.SenderId}");
            handler.Parse(command);
        }

        /// <summary>
        ///     Counts a bad message against a peer and disconnects it after too many.
        /// </summary>
        private static void Strike(NetPeer peer, PeerState state)
        {
            if (state == null)
            {
                return;
            }

            state.Strikes++;
            if (state.Strikes == PeerState.MaxStrikes)
            {
                Disconnect(peer, "You sent too many invalid messages");
            }
        }

        /// <summary>
        ///     Closes a connection, telling the other side why (shown to a kicked player once).
        /// </summary>
        public static void Disconnect(NetPeer peer, string reason)
        {
            PeerState state = peer.Tag as PeerState;
            Log.Info(state?.Player != null
                ? $"Disconnecting player {state.Player.PlayerId} ({state.Player.Username}): {reason}"
                : $"Disconnecting a joining peer: {reason}");
            var writer = new NetDataWriter();
            writer.Put(reason);
            peer.Disconnect(writer);
        }

        public void SendToAllClientsExcept(NetPeer exclude, CommandBase message)
        {
            SendToRegistered(message, exclude);
        }

        /// <summary>
        ///     Sends to every player who passed the join check. Connections that are still joining get nothing.
        /// </summary>
        private void SendToRegistered(CommandBase message, NetPeer exclude)
        {
            byte[] data = null;
            CommandPolicy.DeliveryOf(message.GetType(), out DeliveryMethod method, out byte channel);
            foreach (NetPeer peer in _peers)
            {
                if (!ReferenceEquals(peer, exclude) && peer.Tag is PeerState { Registered: true })
                {
                    data ??= CommandInternal.Instance.Serialize(message);
                    Send(peer, data, method, channel);
                }
            }
        }

        private static void Send(NetPeer peer, byte[] data, DeliveryMethod method, byte channel)
        {
            // Sequenced packets can't be split: anything too big for one goes the reliable way
            if (method != DeliveryMethod.ReliableOrdered && data.Length > peer.GetMaxSinglePacketSize(method))
            {
                method = DeliveryMethod.ReliableOrdered;
                channel = CommandPolicy.EditChannel;
            }

            peer.Send(data, channel, method);
        }

        private void ListenerOnPeerConnectedEvent(NetPeer peer)
        {
            Log.Trace("NetworkManager: OnPeerConnectedEvent");
            if (IsClient)
            {
                if (_serverPeer != null)
                {
                    // Only the one connection we made
                    peer.Disconnect();
                    return;
                }

                _serverPeer = peer;
                _connectWait.Reset();
                ClientConnectSuccessfulEvent?.Invoke();
            }
            // Host: the registration deadline is checked in ProcessEvents (SweepPending)
        }

        private void ListenerOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Log.Trace($"NetworkManager: OnPeerDisconnectedEvent [{disconnectInfo.Reason}]");
            if (IsClient)
            {
                if (_serverPeer != null && !ReferenceEquals(peer, _serverPeer))
                {
                    return;
                }

                // A kick or ban carries its reason as a string
                if (disconnectInfo.Reason == DisconnectReason.RemoteConnectionClose ||
                    disconnectInfo.Reason == DisconnectReason.ConnectionRejected)
                {
                    try
                    {
                        if (disconnectInfo.AdditionalData != null && disconnectInfo.AdditionalData.AvailableBytes > 0)
                        {
                            NetworkInterface.DisconnectNotice =
                                TextRules.CleanChat(disconnectInfo.AdditionalData.GetString(200), 200);
                        }
                    }
                    catch (Exception)
                    {
                        // A plain disconnect has no reason
                    }
                }

                _serverPeer = null;
                _connectWait.Reset();
                ClientDisconnectEvent?.Invoke();
            }
            else if (IsServer)
            {
                _peers.Remove(peer);
                RemotePlayer player = (peer.Tag as PeerState)?.Player;
                player?.HandleDisconnect();
                NetworkInterface.Instance.PlayerDisconnected(player);
            }
        }

        private void ListenerOnNetworkErrorEvent(IPEndPoint endpoint, SocketError socketError)
        {
            // A warning: errors pop up in the game, and the address must never be shown there or logged
            RateLimitedLog.Warn("socket:" + socketError, $"Network error: {socketError}");
        }

        private void ListenerOnNetworkLatencyUpdateEvent(NetPeer peer, int latency)
        {
            if (peer.Tag is PeerState { Player: { } player })
            {
                player.Latency = latency;
            }
        }

        private void ListenerOnNetworkReceiveUnconnectedEvent(IPEndPoint remoteEndPoint, NetPacketReader reader,
            UnconnectedMessageType messageType)
        {
            // Only the API server talks to us this way (the port check result)
            if (messageType != UnconnectedMessageType.BasicMessage || !_apiServer.IsApiServer(remoteEndPoint) ||
                reader.AvailableBytes > MaxUnconnectedBytes)
            {
                return;
            }

            try
            {
                ApiCommandBase command = ApiCommand.Instance.Deserialize(reader.GetRemainingBytes());
                Log.Debug($"Received {command.GetType().Name} from API server");
                ApiCommand.Instance.GetApiCommandHandler(command.GetType())?.Parse(command);
            }
            catch (Exception e)
            {
                RateLimitedLog.Warn("api-read", $"Could not read message from API server: {e.Message}");
            }
        }

        private void ListenerOnConnectionRequestEvent(ConnectionRequest request)
        {
            Log.Trace("NetworkManager: OnConnectionRequestEvent");
            // A joiner never accepts connections: it only talks to the host it connected to
            if (!IsServer)
            {
                request.Reject();
                return;
            }

            IPAddress address = request.RemoteEndPoint.Address;
            string key = address.ToString();
            if (NetworkInterface.Instance.IsBanned(address))
            {
                Log.Info("Connection refused: banned by the host");
                Reject(request, "You are banned from this session");
                return;
            }

            if (JoinGuard.IsBlocked(key, out int minutes))
            {
                RateLimitedLog.Warn("blocked", "Connection refused: too many failed join attempts from that address");
                Reject(request, $"Too many failed join attempts. Try again in {minutes} minute(s).");
                return;
            }

            int pending = _peers.Count(p => p.Tag is PeerState { Registered: false });
            int pendingHere = _peers.Count(p => p.Tag is PeerState { Registered: false } s && s.Address == key);
            int maxPlayers = ApiServer.GetMaxPlayers();
            // The host counts as one; joining peers hold a slot too, so a burst can't overfill the session
            if (_peers.Count >= MaxPeers || (maxPlayers > 0 && 1 + _peers.Count >= maxPlayers))
            {
                Log.Info("Connection refused: the session is full");
                Reject(request, "The session is full");
                return;
            }

            if (pending >= MaxPendingPeers || pendingHere >= MaxPendingPerAddress)
            {
                RateLimitedLog.Warn("pending-limit", "Connection refused: too many connections still joining");
                Reject(request, "The host is busy, try again in a moment");
                return;
            }

            NetPeer peer = request.AcceptIfKey(ConnectionKey);
            if (peer != null)
            {
                peer.Tag = new PeerState(key);
                _peers.Add(peer);
            }
        }

        private static void Reject(ConnectionRequest request, string reason)
        {
            var writer = new NetDataWriter();
            writer.Put(reason);
            request.Reject(writer);
        }

        /// <summary>
        ///     Host: closes connections that didn't pass the join check in time.
        /// </summary>
        private void SweepPending()
        {
            if (_sinceSweep.Elapsed.TotalSeconds < 0.5)
            {
                return;
            }

            _sinceSweep.Restart();
            foreach (NetPeer peer in _peers.ToList())
            {
                if (peer.Tag is PeerState { Registered: false } state &&
                    state.Connected.Elapsed.TotalSeconds > RegisterTimeoutSeconds)
                {
                    Log.Info($"A joining peer did not register within {RegisterTimeoutSeconds} seconds");
                    Disconnect(peer, "Joining took too long");
                    _peers.Remove(peer);
                }
            }
        }

        public delegate bool OnNatHolePunchSuccessful();

        public delegate bool OnNatHolePunchFailed();

        public delegate bool OnClientConnectSuccessful();

        public delegate bool OnClientConnectFailed();

        public delegate bool OnClientDisconnect();

        public delegate bool OnClientSetupFailed();

        public void ProcessEvents()
        {
            if (_stopped)
            {
                return;
            }

            // The event handlers below can end the session (which calls Stop), so check _stopped after each
            try
            {
                if (_resolveTask != null && _resolveTask.IsCompleted)
                {
                    Task<ResolvedEndpoints> task = _resolveTask;
                    _resolveTask = null;
                    ContinueNatConnect(task.Status == TaskStatus.RanToCompletion ? task.Result : null);
                }

                if (!_stopped && _pollNatEvent)
                {
                    _netManager.NatPunchModule.PollEvents();
                }

                if (!_stopped && _natWait.IsRunning && _natWait.Elapsed.TotalSeconds > NatTimeoutSeconds)
                {
                    _natWait.Reset();
                    Log.Debug("NAT hole punch failed, trying direct connect");
                    _pollNatEvent = false;
                    _connectEndpoint = _directEndpoint;
                    NatHolePunchFailedEvent?.Invoke();
                }

                if (!_stopped && _connectWait.IsRunning && _connectWait.Elapsed.TotalSeconds > ConnectTimeoutSeconds)
                {
                    _connectWait.Reset();
                    Log.Debug("Connecting to the server timed out");
                    ClientConnectFailedEvent?.Invoke();
                }

                if (!_stopped)
                {
                    _netManager.PollEvents();
                }

                if (!_stopped && IsServer)
                {
                    SweepPending();
                }
            }
            catch (Exception e)
            {
                RateLimitedLog.Warn("poll", $"Network update failed: {e.GetType().Name}: {e.Message}");
            }

            if (_stopped)
            {
                return;
            }

            try
            {
                // Trigger keepalive to api server
                _apiServer.KeepAlive();
            }
            catch (Exception e)
            {
                RateLimitedLog.Warn("keepalive", $"API server keep-alive failed: {e.Message}");
            }
        }

        public void SendToAllClients(CommandBase message)
        {
            SendToRegistered(message, null);
            Log.Trace($"Sending {message.GetType().Name} to all clients");
        }

        public void SendToClient(NetPeer peer, CommandBase message)
        {
            byte[] data = CommandInternal.Instance.Serialize(message);
            CommandPolicy.DeliveryOf(message.GetType(), out DeliveryMethod method, out byte channel);
            Send(peer, data, method, channel);

            if (message is WorldTransferCommand { NewTransfer: false })
            {
                // Due to performance reasons, log "WorldTransferCommand" only on trace level (after logging once)
                Log.Trace($"Sending {message.GetType().Name} to a client");
            }
            else
            {
                RemotePlayer player = (peer.Tag as PeerState)?.Player;
                Log.Debug($"Sending {message.GetType().Name} to {(player != null ? $"player {player.PlayerId}" : "a joining peer")}");
            }
        }

        public void SendToServer(CommandBase message)
        {
            NetPeer server = _serverPeer;
            if (server == null || server.ConnectionState != ConnectionState.Connected)
            {
                Log.Debug($"Not connected to the server, dropping {message.GetType().Name}");
                return;
            }

            byte[] data = CommandInternal.Instance.Serialize(message);
            CommandPolicy.DeliveryOf(message.GetType(), out DeliveryMethod method, out byte channel);
            Send(server, data, method, channel);

            Log.Trace($"Sending {message.GetType().Name} to server");
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

            // Update the console to let the user know the server is running
            Log.Info("The server has started.");
            Chat.Instance.PrintGameMessage("CS2M.NetworkManager.ServerStarted".Translate());

            // The host punches back by itself when the API server introduces a joiner. The listener and polling
            // only drain the module's event queue, which would otherwise grow with every join.
            _netManager.NatPunchModule.Init(new EventBasedNatPunchListener());
            _pollNatEvent = true;

            // Register with the API server (server browser, joining by token) and check the port from outside
            _apiServer.Start(_connectionConfig);

            return true;
        }

        public void Stop()
        {
            _stopped = true;
            _natWait.Reset();
            _connectWait.Reset();
            _resolveTask = null;
            _apiServer.Stop();
            _netManager.Stop();
            _peers.Clear();
            _serverPeer = null;
            Log.Info("NetworkManager stopped.");
        }
    }
}
