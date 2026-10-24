using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // The heightmap is changed in TerrainBrushApplySystem on the game's update, not on the network callback
    public class TerrainBrushHandler : GuardedHandler<TerrainBrushCommand>
    {
        protected override bool Validate(TerrainBrushCommand command) => CommandValidation.Validate(command);
        protected override void Apply(TerrainBrushCommand command) => TerrainBrushApplySystem.Pending.Enqueue(command);
    }

    public class WaterSourceHandler : GuardedHandler<WaterSourceCommand>
    {
        protected override bool Validate(WaterSourceCommand command) => CommandValidation.Validate(command);
        protected override void Apply(WaterSourceCommand command) => WaterSourceApplySystem.Pending.Enqueue(command);
    }
}
