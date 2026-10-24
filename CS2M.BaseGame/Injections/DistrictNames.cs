using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // host to joiners only
    public class DistrictNamesHandler : GuardedHandler<DistrictNamesCommand>
    {
        protected override bool Validate(DistrictNamesCommand command) => CommandValidation.Validate(command);
        protected override void Apply(DistrictNamesCommand command) => DistrictNameSyncSystem.Pending.Enqueue(command);
    }
}
