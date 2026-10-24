using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class NetDiffHandler : GuardedHandler<NetDiffCommand>
    {
        protected override bool Validate(NetDiffCommand command) => CommandValidation.Validate(command);
        protected override void Apply(NetDiffCommand command) => NetDiffApplySystem.Pending.Enqueue(command);
    }
}
