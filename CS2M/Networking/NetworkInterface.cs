using System.Collections.Generic;
using System.Linq;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands.ApiServer;
using LiteNetLib;

namespace CS2M.Networking
{
    public class NetworkInterface
    {
        private static NetworkInterface _instance;

        public static NetworkInterface Instance => _instance ??= new NetworkInterface();

        public event OnPlayerConnected PlayerConnectedEvent;
        public event OnPlayerDisconnected PlayerDisconnectedEvent;
        public event OnPlayerJoined PlayerJoinedEvent;
        public event OnPlayerLeft PlayerLeftEvent;

        public List<Player> PlayerListConnected = new List<Player>();
        public List<Player> PlayerListJoined = new List<Player>();

        public readonly LocalPlayer LocalPlayer = new LocalPlayer();

        public NetworkInterface()
        {
            PlayerListConnected.Add(LocalPlayer);
            PlayerListJoined.Add(LocalPlayer);
        }

        public void OnUpdate()
        {
            LocalPlayer.OnUpdate();
        }

        public void Connect(ConnectionConfig connectionConfig)
        {
            LocalPlayer.GetServerInfo(connectionConfig);
        }

        public void UpdateLocalPlayerUsername(string username)
        {
            LocalPlayer.UpdateUsername(username);
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

        public RemotePlayer GetPlayerByPeer(NetPeer peer)
        {
            return PlayerListConnected
                .Where(p => p is RemotePlayer)
                .Cast<RemotePlayer>()
                .FirstOrDefault(p => p.NetPeer.Id == peer.Id);
        }

        public bool IsPeerConnected(NetPeer peer)
        {
            return PlayerListConnected
                .Where(p => p is RemotePlayer)
                .Cast<RemotePlayer>()
                .Any(p => p.NetPeer.Id == peer.Id);
        }

        public bool IsPeerJoined(NetPeer peer)
        {
            return PlayerListJoined
                .Where(p => p is RemotePlayer)
                .Cast<RemotePlayer>()
                .Any(p => p.NetPeer.Id == peer.Id);
        }

        public void PlayerConnected(RemotePlayer player)
        {
            Log.Debug($"RemotePlayer '{player.Username}' connected.");
            PlayerListConnected.Add(player);
            PlayerConnectedEvent?.Invoke(player);
        }

        public delegate void OnPlayerConnected(Player player);

        public delegate void OnPlayerDisconnected(Player player);

        public delegate void OnPlayerJoined(Player player);

        public delegate void OnPlayerLeft(Player player);
    }
}