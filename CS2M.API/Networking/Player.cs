namespace CS2M.API.Networking
{
    public class Player
    {
        public int PlayerId { get; set; }

        public string Username { get; set; }

        public long Latency { get; set; }

        public PlayerStatus PlayerStatus { get; set; } = PlayerStatus.INACTIVE;

        public PlayerType PlayerType { get; set; }

        public Player()
        {
        }
    }
}