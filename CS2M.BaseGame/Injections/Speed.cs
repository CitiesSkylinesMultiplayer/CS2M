using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class SpeedHandler : GuardedHandler<SpeedCommand>
    {
        protected override bool Validate(SpeedCommand command) => CommandValidation.Validate(command);
        protected override void Apply(SpeedCommand command) => SpeedSyncSystem.Pending.Enqueue(command);
    }
}
