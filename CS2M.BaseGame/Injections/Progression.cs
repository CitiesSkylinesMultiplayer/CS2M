using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // Host to joiners: the host's progression. Joiners to host: requests the host applies once (never relayed).
    public class ProgressionHandler : GuardedHandler<ProgressionCommand>
    {
        protected override bool Validate(ProgressionCommand command) => CommandValidation.Validate(command);
        protected override void Apply(ProgressionCommand command) => ProgressionSyncSystem.Pending.Enqueue(command);
    }

    public class DevTreeHandler : GuardedHandler<DevTreeCommand>
    {
        protected override bool Validate(DevTreeCommand command) => CommandValidation.Validate(command);
        protected override void Apply(DevTreeCommand command) => ProgressionSyncSystem.Pending.Enqueue(command);
    }

    public class LoanRequestHandler : GuardedHandler<LoanRequestCommand>
    {
        protected override bool Validate(LoanRequestCommand command) => CommandValidation.Validate(command);
        protected override void Apply(LoanRequestCommand command) => ProgressionSyncSystem.Pending.Enqueue(command);
    }

    public class DevTreeRequestHandler : GuardedHandler<DevTreeRequestCommand>
    {
        protected override bool Validate(DevTreeRequestCommand command) => CommandValidation.Validate(command);
        protected override void Apply(DevTreeRequestCommand command) => ProgressionSyncSystem.Pending.Enqueue(command);
    }

    public class MapTilesHandler : GuardedHandler<MapTilesCommand>
    {
        protected override bool Validate(MapTilesCommand command) => CommandValidation.Validate(command);
        protected override void Apply(MapTilesCommand command) => MapTileSyncSystem.Pending.Enqueue(command);
    }

    public class TilePurchaseRequestHandler : GuardedHandler<TilePurchaseRequestCommand>
    {
        protected override bool Validate(TilePurchaseRequestCommand command) => CommandValidation.Validate(command);
        protected override void Apply(TilePurchaseRequestCommand command) => MapTileSyncSystem.Pending.Enqueue(command);
    }
}
