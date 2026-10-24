using System.Collections.Generic;
using System.Reflection;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Budget panel figures come from each game's own simulation, and joiners don't run building upkeep,
    ///     so the host sends its figures every 2 s and joiners write them in just before the UI reads them:
    ///     income and expenses, collected service fees, and per-service upkeep.
    ///     Per-service upkeep is answered from <see cref="TryGetHostUpkeep"/> by a patch in the main mod.
    /// </summary>
    public partial class BudgetFiguresSystem : GameSystemBase
    {
        private const float SendInterval = 2f;

        private static readonly FieldInfo IncomeField =
            typeof(CityServiceBudgetSystem).GetField("m_Income", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ExpensesField =
            typeof(CityServiceBudgetSystem).GetField("m_Expenses", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DepsField =
            typeof(CityServiceBudgetSystem).GetField("m_TempArrayDeps", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FeesField =
            typeof(ServiceFeeSystem).GetField("m_CityServiceFees", BindingFlags.Instance | BindingFlags.NonPublic);

        public static BudgetFiguresCommand Latest;
        private static readonly Dictionary<string, int> HostUpkeep = new Dictionary<string, int>();
        private static PrefabSystem _prefabs;

        private CityServiceBudgetSystem _budgetSystem;
        private ServiceFeeSystem _feeSystem;
        private EntityQuery _serviceQuery;
        private float _lastSend;

        protected override void OnCreate()
        {
            base.OnCreate();
            _budgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            _feeSystem = World.GetOrCreateSystemManaged<ServiceFeeSystem>();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _serviceQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<UIObjectData>(),
                ComponentType.ReadOnly<ServiceData>());
        }

        /// <summary>
        ///     Joiners: the host's estimated upkeep for this service, if known.
        /// </summary>
        public static bool TryGetHostUpkeep(Entity service, out int upkeep)
        {
            upkeep = 0;
            if (Command.CurrentRole != MultiplayerRole.Client || _prefabs == null)
            {
                return false;
            }

            string name = PrefabLookup.Name(_prefabs, service);
            return name != null && HostUpkeep.TryGetValue(name, out upkeep);
        }

        public static void Receive(BudgetFiguresCommand command)
        {
            Latest = command;
            HostUpkeep.Clear();
            for (int i = 0; i < (command.Services?.Length ?? 0) && i < (command.Upkeep?.Length ?? 0); i++)
            {
                if (command.Services[i] != null)
                {
                    HostUpkeep[command.Services[i]] = command.Upkeep[i];
                }
            }
        }

        protected override void OnUpdate()
        {
            MultiplayerRole role = Command.CurrentRole;
            if (role == MultiplayerRole.None || IncomeField == null || ExpensesField == null)
            {
                Latest = null;
                HostUpkeep.Clear();
                return;
            }

            if (DepsField?.GetValue(_budgetSystem) is JobHandle deps)
            {
                deps.Complete();
            }

            var income = (NativeArray<int>)IncomeField.GetValue(_budgetSystem);
            var expenses = (NativeArray<int>)ExpensesField.GetValue(_budgetSystem);
            if (!income.IsCreated || !expenses.IsCreated)
            {
                return;
            }

            if (role == MultiplayerRole.Client)
            {
                if (Latest != null)
                {
                    Copy(Latest.Income, income);
                    Copy(Latest.Expenses, expenses);
                    CopyFees(Latest);
                }

                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastSend < SendInterval)
            {
                return;
            }

            _lastSend = now;
            var services = new List<string>();
            var upkeep = new List<int>();
            // every service the budget panel lists; ServiceBudgetData only holds services whose slider was moved
            using (NativeArray<Entity> serviceEntities = _serviceQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity service in serviceEntities)
                {
                    string name = PrefabLookup.Name(_prefabs, service);
                    if (name == null)
                    {
                        continue;
                    }

                    _budgetSystem.GetEstimatedServiceBudget(service, out int value);
                    services.Add(name);
                    upkeep.Add(value);
                }
            }

            var feeResources = new List<int>();
            var feeValues = new List<float>();
            if (FeesField?.GetValue(_feeSystem) is NativeList<CollectedCityServiceFeeData> fees && fees.IsCreated)
            {
                foreach (CollectedCityServiceFeeData fee in fees)
                {
                    feeResources.Add(fee.m_PlayerResource);
                    feeValues.AddRange(new[]
                    {
                        fee.m_Export, fee.m_Import, fee.m_Internal, fee.m_ExportCount, fee.m_ImportCount, fee.m_InternalCount
                    });
                }
            }

            Command.SendToAll?.Invoke(new BudgetFiguresCommand
            {
                Income = income.ToArray(),
                Expenses = expenses.ToArray(),
                Services = services.ToArray(),
                Upkeep = upkeep.ToArray(),
                FeeResources = feeResources.ToArray(),
                FeeValues = feeValues.ToArray()
            });
        }

        // ServiceFeeSystem rebuilds the fee list from the joiner's own fee collectors whenever it updates; the
        // host's list replaces it until then, so the fee lines of the service rows match the host's
        private void CopyFees(BudgetFiguresCommand command)
        {
            if (command.FeeResources == null || command.FeeValues == null ||
                !(FeesField?.GetValue(_feeSystem) is NativeList<CollectedCityServiceFeeData> fees) || !fees.IsCreated)
            {
                return;
            }

            fees.Clear();
            for (int i = 0; i < command.FeeResources.Length; i++)
            {
                float[] v = command.FeeValues;
                var fee = new CollectedCityServiceFeeData
                {
                    m_PlayerResource = command.FeeResources[i],
                    m_Export = v[i * 6], m_Import = v[i * 6 + 1], m_Internal = v[i * 6 + 2],
                    m_ExportCount = v[i * 6 + 3], m_ImportCount = v[i * 6 + 4], m_InternalCount = v[i * 6 + 5]
                };
                fees.Add(fee);
            }
        }

        private static void Copy(int[] from, NativeArray<int> to)
        {
            if (from == null)
            {
                return;
            }

            for (int i = 0; i < from.Length && i < to.Length; i++)
            {
                to[i] = from[i];
            }
        }
    }
}
