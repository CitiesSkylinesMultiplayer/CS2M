using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class RoadUpgradeHandler : GuardedHandler<RoadUpgradeCommand>
    {
        protected override bool Validate(RoadUpgradeCommand command) => CommandValidation.Validate(command);
        protected override void Apply(RoadUpgradeCommand command) => RoadUpgradeApplySystem.Pending.Enqueue(command);
    }
}
