namespace CS2M.API.Networking
{
    public abstract class Player
    {
        public event OnPlayerStatusChanged PlayerStatusChangedEvent;

        public event OnPlayerTypeChanged PlayerTypeChangedEvent;

        public int PlayerId { get; set; }

        public string Username { get; protected set; }

        public long Latency { get; set; }


        private PlayerStatus _playerStatus = PlayerStatus.INACTIVE;
        public PlayerStatus PlayerStatus
        {
            get => _playerStatus;
            protected set
            {
                PlayerStatusChangedEvent?.Invoke(_playerStatus, value);
                _playerStatus = value;
            }
        }

        private PlayerType _playerType = PlayerType.NONE;
        public PlayerType PlayerType
        {
            get => _playerType;
            protected set
            {
                PlayerTypeChangedEvent?.Invoke(_playerType, value);
                _playerType = value;
            }
        }

        public Player()
        {
        }

        public delegate void OnPlayerStatusChanged(PlayerStatus oldPlayerStatus, PlayerStatus newPlayerStatus);

        public delegate void OnPlayerTypeChanged(PlayerType oldPlayerType, PlayerType newPlayerType);
    }
}