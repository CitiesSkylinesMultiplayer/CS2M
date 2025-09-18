using CS2M.API.Networking;
using LiteNetLib;

namespace CS2M.Networking
{
    public class RemotePlayer : Player
    {
        public NetPeer NetPeer { get; }

        public RemotePlayer(NetPeer peer, string username, PlayerType playerType) : base()
        {
            NetPeer = peer;
            Username = username;
            PlayerType = playerType;
            PlayerStatusChangedEvent += PlayerStatusChanged;
            PlayerTypeChangedEvent += PlayerTypeChanged;
        }

        public RemotePlayer(string username, PlayerType playerType) : base()
        {
            NetPeer = null;
            Username = username;
            PlayerType = playerType;
            PlayerStatusChangedEvent += PlayerStatusChanged;
            PlayerTypeChangedEvent += PlayerTypeChanged;
        }

        public void PlayerStatusChanged(PlayerStatus oldPlayerStatus, PlayerStatus newPlayerStatus)
        {
            Log.Trace($"RemotePlayer: {Username} ({PlayerId}) changed player status from {oldPlayerStatus} to {newPlayerStatus}");
        }

        public void PlayerTypeChanged(PlayerType oldPlayerType, PlayerType newPlayerType)
        {
            Log.ErrorWithStackTrace($"RemotePlayer: {Username} ({PlayerId}) Player type for remote player shouldn't be changed after initialization");
        }

        public void HandleConnect()
        {
        }

        public void Disconnect()
        {
            NetPeer.Disconnect();
        }

        public void HandleDisconnect()
        {
        }
    }
}