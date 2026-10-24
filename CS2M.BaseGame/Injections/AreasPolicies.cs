using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class AreaChangeHandler : GuardedHandler<AreaChangeCommand>
    {
        protected override bool Validate(AreaChangeCommand command) => CommandValidation.Validate(command);
        protected override void Apply(AreaChangeCommand command) => AreaApplySystem.Pending.Enqueue(command);
    }

    public class PolicyHandler : GuardedHandler<PolicyCommand>
    {
        protected override bool Validate(PolicyCommand command) => CommandValidation.Validate(command);
        protected override void Apply(PolicyCommand command) => PolicySyncSystem.Pending.Enqueue(command);
    }
}
