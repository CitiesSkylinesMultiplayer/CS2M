using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class PlaceHandler : GuardedHandler<PlaceCommand>
    {
        protected override bool Validate(PlaceCommand command) => CommandValidation.Validate(command);
        protected override void Apply(PlaceCommand command) => PlaceApplySystem.Pending.Enqueue(command);
    }
}
