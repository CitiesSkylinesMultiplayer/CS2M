using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // The host's decisions about buildings and money (host to joiners only), handled by HostAuthoritySystem.
    public class SpawnHandler : GuardedHandler<SpawnCommand>
    {
        protected override bool Validate(SpawnCommand command) => CommandValidation.Validate(command);
        protected override void Apply(SpawnCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }

    public class LevelUpHandler : GuardedHandler<LevelUpCommand>
    {
        protected override bool Validate(LevelUpCommand command) => CommandValidation.Validate(command);
        protected override void Apply(LevelUpCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }

    public class AbandonHandler : GuardedHandler<AbandonCommand>
    {
        protected override bool Validate(AbandonCommand command) => CommandValidation.Validate(command);
        protected override void Apply(AbandonCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }

    public class MoneyHandler : GuardedHandler<MoneyCommand>
    {
        protected override void Apply(MoneyCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }

    // joiner to host only; the new balance reaches everyone through MoneyCommand. Amounts are checked when applied
    public class ChargeHandler : GuardedHandler<ChargeCommand>
    {
        protected override void Apply(ChargeCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }
}
