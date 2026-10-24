using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class BuildingPolicyHandler : GuardedHandler<BuildingPolicyCommand>
    {
        protected override bool Validate(BuildingPolicyCommand command) => CommandValidation.Validate(command);
        protected override void Apply(BuildingPolicyCommand command) => BuildingSettingsSyncSystem.Pending.Enqueue(command);
    }

    public class RenameHandler : GuardedHandler<RenameCommand>
    {
        protected override bool Validate(RenameCommand command) => CommandValidation.Validate(command);
        protected override void Apply(RenameCommand command) => NameSyncSystem.Pending.Enqueue(command);
    }
}
