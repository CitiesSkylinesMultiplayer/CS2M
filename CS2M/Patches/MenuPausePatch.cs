using System.Reflection;
using CS2M.API.Commands;
using CS2M.BaseGame;
using Game.Simulation;
using Game.UI.InGame;
using HarmonyLib;

namespace CS2M.Patches
{
    /// <summary>
    ///     The game pauses itself while its window is in the background or a pause-type menu is open
    ///     (TimeUISystem.OnUpdate). In a session that would pause everyone through the speed sync, so the
    ///     forced pause is undone; the player shows as "in menu" / "away" instead. Pressing Pause still pauses.
    /// </summary>
    public static class MenuPausePatch
    {
        private static readonly FieldInfo HasFocus = AccessTools.Field(typeof(TimeUISystem), "m_HasFocus");
        private static readonly FieldInfo Barrier = AccessTools.Field(typeof(TimeUISystem), "m_SimulationPausedBarrierBinding");
        private static readonly FieldInfo Simulation = AccessTools.Field(typeof(TimeUISystem), "m_SimulationSystem");
        private static readonly FieldInfo UnpausedBefore = AccessTools.Field(typeof(TimeUISystem), "m_UnpausedBeforeForcedPause");

        public static void Apply(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(TimeUISystem), "OnUpdate");
            if (target == null || HasFocus == null || Barrier == null || Simulation == null)
            {
                Log.Warn("TimeUISystem fields not found: menus will still pause multiplayer sessions");
                return;
            }

            harmony.Patch(target, new HarmonyMethod(typeof(MenuPausePatch), nameof(Prefix)),
                new HarmonyMethod(typeof(MenuPausePatch), nameof(Postfix)));
        }

        public static void Prefix(TimeUISystem __instance, out float __state)
        {
            __state = (Simulation.GetValue(__instance) as SimulationSystem)?.selectedSpeed ?? 0f;
        }

        public static void Postfix(TimeUISystem __instance, float __state)
        {
            bool away = !(bool)HasFocus.GetValue(__instance);
            bool inMenu = Barrier.GetValue(__instance) is Colossal.UI.Binding.EventBindingBase barrier && barrier.observerCount > 0;
            Presence.Away = away;
            Presence.InMenu = inMenu;
            if (Command.CurrentRole == MultiplayerRole.None || !(away || inMenu))
            {
                return;
            }

            // Keep the speed it had before this update (0 stays 0 if the player had paused themselves)
            if (Simulation.GetValue(__instance) is SimulationSystem simulation && simulation.selectedSpeed != __state)
            {
                simulation.selectedSpeed = __state;
            }

            UnpausedBefore?.SetValue(__instance, false);
        }
    }
}
