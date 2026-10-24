using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     Game speed (0 = paused). Sent by: SpeedSyncSystem, when a player changes it, and by the host every
    ///     few seconds so everyone stays on the host's speed.
    /// </summary>
    public class SpeedCommand : CommandBase
    {
        public float Speed { get; set; }
    }
}
