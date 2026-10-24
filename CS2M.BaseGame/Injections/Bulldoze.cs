using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class BulldozeHandler : GuardedHandler<BulldozeCommand>
    {
        protected override bool Validate(BulldozeCommand command) => CommandValidation.Validate(command);
        protected override void Apply(BulldozeCommand command) => BulldozeApplySystem.Pending.Enqueue(command);
    }
}
