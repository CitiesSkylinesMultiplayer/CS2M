using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // Transport lines; all applied in order by TransitLineApplySystem on the game update.
    public class TransitLineHandler : GuardedHandler<TransitLineCommand>
    {
        protected override bool Validate(TransitLineCommand command) => CommandValidation.Validate(command);
        protected override void Apply(TransitLineCommand command) => TransitLineApplySystem.Pending.Enqueue(command);
    }

    public class TransitLineDeleteHandler : GuardedHandler<TransitLineDeleteCommand>
    {
        protected override bool Validate(TransitLineDeleteCommand command) => CommandValidation.Validate(command);
        protected override void Apply(TransitLineDeleteCommand command) => TransitLineApplySystem.Pending.Enqueue(command);
    }

    public class TransitLineSettingsHandler : GuardedHandler<TransitLineSettingsCommand>
    {
        protected override bool Validate(TransitLineSettingsCommand command) => CommandValidation.Validate(command);
        protected override void Apply(TransitLineSettingsCommand command) => TransitLineApplySystem.Pending.Enqueue(command);
    }
}
