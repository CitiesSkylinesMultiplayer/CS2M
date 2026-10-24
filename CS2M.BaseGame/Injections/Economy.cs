using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class TaxHandler : GuardedHandler<TaxCommand>
    {
        protected override bool Validate(TaxCommand command) => CommandValidation.Validate(command);
        protected override void Apply(TaxCommand command) => EconomySyncSystem.Pending.Enqueue(command);
    }

    public class BudgetHandler : GuardedHandler<BudgetCommand>
    {
        protected override bool Validate(BudgetCommand command) => CommandValidation.Validate(command);
        protected override void Apply(BudgetCommand command) => EconomySyncSystem.Pending.Enqueue(command);
    }

    // host to joiners only
    public class PopulationHandler : GuardedHandler<PopulationCommand>
    {
        protected override bool Validate(PopulationCommand command) => CommandValidation.Validate(command);
        protected override void Apply(PopulationCommand command) => PopulationDisplaySystem.Latest = command;
    }

    // host to joiners only
    public class BudgetFiguresHandler : GuardedHandler<BudgetFiguresCommand>
    {
        protected override bool Validate(BudgetFiguresCommand command) => CommandValidation.Validate(command);
        protected override void Apply(BudgetFiguresCommand command) => BudgetFiguresSystem.Receive(command);
    }
}
