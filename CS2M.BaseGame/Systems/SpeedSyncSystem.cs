using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Simulation;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Keeps pause and game speed the same for everyone.
    /// </summary>
    public partial class SpeedSyncSystem : GameSystemBase
    {
        public static readonly InboundQueue<SpeedCommand> Pending = new InboundQueue<SpeedCommand>("SpeedSyncSystem", 64, 16);

        private const float HostRepeat = 5f;

        private SimulationSystem _simulationSystem;
        private float _known = -1f;
        private float _lastHostSend;

        protected override void OnCreate()
        {
            base.OnCreate();
            _simulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                _known = -1f;
                return;
            }

            while (Pending.TryDequeue(out SpeedCommand command))
            {
                try
                {
                    if (command.Speed != _simulationSystem.selectedSpeed)
                    {
                        _simulationSystem.selectedSpeed = command.Speed;
                        BaseGameLog.Info($"Game speed set to {command.Speed} by another player");
                    }

                    _known = command.Speed;
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(SpeedSyncSystem), e);
                }
            }

            float speed = _simulationSystem.selectedSpeed;
            float now = UnityEngine.Time.realtimeSinceStartup;
            bool changedHere = speed != _known;
            bool hostRepeat = Command.CurrentRole == MultiplayerRole.Server && now - _lastHostSend > HostRepeat;
            if (changedHere || hostRepeat)
            {
                Command.SendToAll?.Invoke(new SpeedCommand { Speed = speed });
                _known = speed;
                if (Command.CurrentRole == MultiplayerRole.Server)
                {
                    _lastHostSend = now;
                }
            }
        }
    }
}
