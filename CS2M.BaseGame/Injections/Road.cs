using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class RoadHandler : GuardedHandler<RoadCommand>
    {
        protected override bool Validate(RoadCommand command) => CommandValidation.Validate(command);
        protected override void Apply(RoadCommand command) => RoadApplySystem.Pending.Enqueue(command);
    }
}
