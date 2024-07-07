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
        }

        public RemotePlayer(string username, PlayerType playerType) : base()
        {
            NetPeer = null;
        }
    }
}