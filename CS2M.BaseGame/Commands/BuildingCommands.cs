using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     A growable building the host's zone spawner placed. Sent by: SpawnCaptureSystem (host only).
    /// </summary>
    public class SpawnCommand : CommandBase
    {
        public string Prefab { get; set; }
        public uint CreationFlags { get; set; }
        public int RandomSeed { get; set; }

        /// <summary>
        ///     Set when the building attaches to an existing one (CreationFlags.Attach).
        /// </summary>
        public NetRef Attached { get; set; }

        public float[] Position { get; set; }
        public float[] LocalPosition { get; set; }
        public float[] Scale { get; set; }
        public float[] Rotation { get; set; }
        public float[] LocalRotation { get; set; }
        public float Elevation { get; set; }
        public float Intensity { get; set; }
        public float Age { get; set; }
        public bool IsDecoration { get; set; }
        public int ParentMesh { get; set; }
        public int GroupIndex { get; set; }
        public int Probability { get; set; }
        public int PrefabSubIndex { get; set; }
    }

    /// <summary>
    ///     The host's upkeep system chose a level-up: the building starts becoming NewPrefab.
    ///     Sent by: HostAuthoritySystem (host only).
    /// </summary>
    public class LevelUpCommand : CommandBase
    {
        public NetRef Building { get; set; }
        public string NewPrefab { get; set; }
        public byte Progress { get; set; }
        public byte Speed { get; set; }
    }

    /// <summary>
    ///     The host's upkeep system abandoned a building. Sent by: HostAuthoritySystem (host only).
    /// </summary>
    public class AbandonCommand : CommandBase
    {
        public NetRef Building { get; set; }
        public uint AbandonmentTime { get; set; }
    }

    /// <summary>
    ///     The host's city money; everyone shows the host's balance. Sent by: HostAuthoritySystem (host only).
    /// </summary>
    public class MoneyCommand : CommandBase
    {
        public int Money { get; set; }
    }

    /// <summary>
    ///     What a joiner's tool action cost, so the host can charge it. Not relayed to other players.
    ///     Sent by: ChargeCaptureSystem (joiners only).
    /// </summary>
    public class ChargeCommand : CommandBase
    {
        public int Amount { get; set; }
    }
}
