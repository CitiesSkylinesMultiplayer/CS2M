using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class ColorHandler : GuardedHandler<ColorCommand>
    {
        protected override bool Validate(ColorCommand command) => CommandValidation.Validate(command);
        protected override void Apply(ColorCommand command) => ColorSyncSystem.Pending.Enqueue(command);
    }
}
