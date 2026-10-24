using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     The full tax rate table (TaxSystem.m_TaxRates). Sent by: EconomySyncSystem, on the game that changed a tax slider.
    /// </summary>
    public class TaxCommand : CommandBase
    {
        public int[] Rates { get; set; }
    }

    /// <summary>
    ///     Service budgets: Services[i] (prefab name) is at Budgets[i] percent.
    ///     Sent by: EconomySyncSystem, on the game that changed a budget.
    /// </summary>
    public class BudgetCommand : CommandBase
    {
        public string[] Services { get; set; }
        public int[] Budgets { get; set; }
    }

    /// <summary>
    ///     The host's population figures, shown on the joiners' top bar. Sent by: HostAuthoritySystem (host only).
    /// </summary>
    public class PopulationCommand : CommandBase
    {
        public int Population { get; set; }
        public int PopulationWithMoveIn { get; set; }
        public int AverageHappiness { get; set; }
        public int AverageHealth { get; set; }
    }

    /// <summary>
    ///     The host's budget figures, shown on the joiners' budget panels: income and expense per category
    ///     (CityServiceBudgetSystem.m_Income / m_Expenses) and estimated monthly upkeep per service.
    ///     Sent by: BudgetFiguresSystem (host only).
    /// </summary>
    public class BudgetFiguresCommand : CommandBase
    {
        public int[] Income { get; set; }
        public int[] Expenses { get; set; }
        public string[] Services { get; set; }
        public int[] Upkeep { get; set; }

        /// <summary>Service fee totals (ServiceFeeSystem): the PlayerResource of each entry.</summary>
        public int[] FeeResources { get; set; }

        /// <summary>Per entry: export, import, internal and their counts (CollectedCityServiceFeeData order).</summary>
        public float[] FeeValues { get; set; }
    }
}
