using System.Collections.Generic;
using System.Linq;
using Colossal;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Commands.Data.Internal;
using CS2M.Helpers;
using LiteNetLib;
using Unity.Entities;

namespace CS2M.Networking
{
    public class NetworkInterface
    {
        public delegate void OnPlayerConnected(Player player);

        public delegate void OnPlayerDisconnected(Player player);

        public delegate void OnPlayerJoined(Player player);

        public delegate void OnPlayerLeft(Player player);

        private static NetworkInterface _instance;

        public readonly LocalPlayer LocalPlayer = new();

        /// <summary>
        ///     List of all players, which are connected on network level
        /// </summary>
        public List<Player> PlayerListConnected = new();

        /// <summary>
        ///     List of all players, which are connected on game level
        /// </summary>
        public List<Player> PlayerListJoined = new();

        public NetworkInterface()
        {
            PlayerListConnected.Add(LocalPlayer);
            PlayerListJoined.Add(LocalPlayer);
        }

        public static NetworkInterface Instance => _instance ??= new NetworkInterface();

        /// <summary>
        ///     Event is triggered, when a player is connected on the network level
        /// </summary>
        public event OnPlayerConnected PlayerConnectedEvent;

        /// <summary>
        ///     Event is triggered, when a player disconnects on the network level
        /// </summary>
        public event OnPlayerDisconnected PlayerDisconnectedEvent;

        /// <summary>
        ///     Event is triggered, when a player joins on the game level
        /// </summary>
        public event OnPlayerJoined PlayerJoinedEvent;

        /// <summary>
        ///     Event is triggered, when a player leaves on the game level
        /// </summary>
        public event OnPlayerLeft PlayerLeftEvent;

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

        public void StopServer()
        {
            LocalPlayer.Inactive();
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

            // Get max packet size from MTU discovery
            int maxPacketSize = player.NetPeer.GetMaxSinglePacketSize(DeliveryMethod.ReliableOrdered);
            maxPacketSize -= 25; // Maximum packet overhead as computed and tested in `PacketSizeOverhead` unit test

            // Send world
            TaskManager.instance.EnqueueTask("LoadMap", async () =>
            {
                SaveLoadHelper saveLoadHelper =
                    World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<SaveLoadHelper>();
                SlicedPacketStream stream = await saveLoadHelper.SaveGame(maxPacketSize);
                int remainingBytes = (int)stream.Length;
                bool newTransfer = true;

                Log.Debug($"Sending world with size of {stream.Length} bytes. Slice size: {maxPacketSize}");
                foreach (byte[] slice in stream.GetSlices())
                {
                    remainingBytes -= slice.Length;
                    var cmd = new WorldTransferCommand
                    {
                        WorldSlice = slice,
                        RemainingBytes = remainingBytes,
                        NewTransfer = newTransfer
                    };

                    CommandInternal.Instance.SendToClient(player, cmd);

                    newTransfer = false;
                }
            });
        }
    }
}
