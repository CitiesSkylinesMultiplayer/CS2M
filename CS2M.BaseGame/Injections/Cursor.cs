using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // names and positions are cleaned in CursorSystem.OnRemoteCursor
    public class CursorHandler : GuardedHandler<CursorCommand>
    {
        protected override void Apply(CursorCommand command) => CursorSystem.OnRemoteCursor(command);
    }
}
