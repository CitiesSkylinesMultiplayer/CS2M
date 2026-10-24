using CS2M.BaseGame.Systems;
using Game.Simulation;
using HarmonyLib;
using Unity.Entities;

namespace CS2M.Patches
{
    /// <summary>
    ///     Joiners don't run building upkeep, so their per-service upkeep estimate would be wrong; show the host's.
    ///     Registered in code rather than with [HarmonyPatch]: the attribute's ArgumentType argument makes ILRepack
    ///     resolve 0Harmony while reading CS2M.dll, which it can't from the output folder.
    /// </summary>
    public static class ServiceUpkeepPatch
    {
        public static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(CityServiceBudgetSystem), nameof(CityServiceBudgetSystem.GetEstimatedServiceBudget),
                new[] { typeof(Entity), typeof(int).MakeByRefType() });
            if (target == null)
            {
                Log.Warn("CityServiceBudgetSystem.GetEstimatedServiceBudget not found: joiners show their own service costs");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ServiceUpkeepPatch), nameof(Postfix)));
        }

        public static void Postfix(Entity servicePrefab, ref int upkeep)
        {
            if (BudgetFiguresSystem.TryGetHostUpkeep(servicePrefab, out int host))
            {
                upkeep = host;
            }
        }
    }
}
