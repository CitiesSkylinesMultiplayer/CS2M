using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // Entity changes happen in ZoneApplySystem on the game's update, not on the network callback
    public class ZoneCellsHandler : GuardedHandler<ZoneCellsCommand>
    {
        protected override bool Validate(ZoneCellsCommand command) => CommandValidation.Validate(command);
        protected override void Apply(ZoneCellsCommand command) => ZoneApplySystem.Pending.Enqueue(command);
    }
}
