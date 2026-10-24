using System.Collections.Generic;
using System.Reflection;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.City;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Keeps taxes and service budgets the same for everyone: a local change is sent, a received one is
    ///     applied. Checked twice a second, the same way as game speed.
    /// </summary>
    public partial class EconomySyncSystem : GameSystemBase
    {
        public static readonly InboundQueue<CommandBase> Pending = new InboundQueue<CommandBase>("EconomySyncSystem", 256, 16);

        private const float CheckInterval = 0.5f;

        // the service budget slider's range in the game's budget panel, in percent
        public const int MinBudget = 50;
        public const int MaxBudget = 150;

        // TaxSystem keeps every rate in one private NativeArray<int>
        private static readonly FieldInfo TaxRatesField =
            typeof(TaxSystem).GetField("m_TaxRates", BindingFlags.Instance | BindingFlags.NonPublic);

        private TaxSystem _taxSystem;
        private CityServiceBudgetSystem _budgetSystem;
        private PrefabSystem _prefabSystem;
        private EntityQuery _budgetQuery;
        private EntityQuery _taxParameters;

        private int[] _knownTaxes;
        private Dictionary<string, int> _knownBudgets;
        private float _lastCheck;

        protected override void OnCreate()
        {
            base.OnCreate();
            _taxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            _budgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _budgetQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceBudgetData>());
            _taxParameters = GetEntityQuery(ComponentType.ReadOnly<TaxParameterData>());
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                _knownTaxes = null;
                _knownBudgets = null;
                return;
            }

            while (Pending.TryDequeue(out CommandBase command))
            {
                try
                {
                    if (command is TaxCommand tax)
                    {
                        if (SetTaxes(tax.Rates))
                        {
                            // what the game kept after applying its own limits
                            _knownTaxes = GetTaxes();
                            BaseGameLog.Info("Taxes changed by another player");
                        }
                    }
                    else if (command is BudgetCommand budget)
                    {
                        // the handler made both arrays the same length and clamped the values
                        for (int i = 0; i < budget.Services.Length; i++)
                        {
                            Entity service = PrefabLookup.Find(EntityManager, _prefabSystem, budget.Services[i]);
                            if (service != Entity.Null)
                            {
                                _budgetSystem.SetServiceBudget(service, budget.Budgets[i]);
                            }
                        }

                        _knownBudgets = GetBudgets();
                        BaseGameLog.Info("Service budgets changed by another player");
                    }
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(EconomySyncSystem), e);
                }
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastCheck < CheckInterval)
            {
                return;
            }

            _lastCheck = now;
            int[] taxes = GetTaxes();
            if (taxes != null)
            {
                if (_knownTaxes != null && !Same(taxes, _knownTaxes))
                {
                    Command.SendToAll?.Invoke(new TaxCommand { Rates = taxes });
                }

                _knownTaxes = taxes;
            }

            Dictionary<string, int> budgets = GetBudgets();
            if (_knownBudgets != null && !Same(budgets, _knownBudgets))
            {
                var services = new List<string>();
                var values = new List<int>();
                foreach (KeyValuePair<string, int> pair in budgets)
                {
                    services.Add(pair.Key);
                    values.Add(pair.Value);
                }

                Command.SendToAll?.Invoke(new BudgetCommand { Services = services.ToArray(), Budgets = values.ToArray() });
            }

            _knownBudgets = budgets;
        }

        private int[] GetTaxes()
        {
            if (TaxRatesField == null)
            {
                return null;
            }

            var rates = (NativeArray<int>)TaxRatesField.GetValue(_taxSystem);
            return rates.IsCreated ? rates.ToArray() : null;
        }

        private bool SetTaxes(int[] values)
        {
            if (TaxRatesField == null || values == null)
            {
                return false;
            }

            // jobs read the table; let them finish first
            _taxSystem.Readers.Complete();

            var rates = (NativeArray<int>)TaxRatesField.GetValue(_taxSystem);
            if (!rates.IsCreated || values.Length != rates.Length)
            {
                Inbound.Warn("tax-length", $"Taxes received with {values.Length} rates, this game has {rates.Length}; ignored");
                return false;
            }

            for (int i = 0; i < rates.Length; i++)
            {
                rates[i] = values[i];
            }

            // the same limits the tax sliders get: setting the overall rate clamps it to TaxParameterData and
            // then clamps every area, job level and resource rate under it (EnsureAreaTaxRateLimits)
            if (!_taxParameters.IsEmptyIgnoreFilter)
            {
                _taxSystem.GetTaxParameterData();
                _taxSystem.TaxRate = _taxSystem.TaxRate;
            }

            return true;
        }

        private Dictionary<string, int> GetBudgets()
        {
            var budgets = new Dictionary<string, int>();
            if (_budgetQuery.IsEmptyIgnoreFilter)
            {
                return budgets;
            }

            using (NativeArray<Entity> entities = _budgetQuery.ToEntityArray(Allocator.Temp))
            {
                DynamicBuffer<ServiceBudgetData> buffer = EntityManager.GetBuffer<ServiceBudgetData>(entities[0], true);
                foreach (ServiceBudgetData data in buffer)
                {
                    string name = PrefabLookup.Name(_prefabSystem, data.m_Service);
                    if (name != null)
                    {
                        budgets[name] = data.m_Budget;
                    }
                }
            }

            return budgets;
        }

        private static bool Same(int[] a, int[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, int> pair in a)
            {
                if (!b.TryGetValue(pair.Key, out int value) || value != pair.Value)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    ///     Joiners: puts the host's population into the city just before the top bar reads it.
    /// </summary>
    public partial class PopulationDisplaySystem : GameSystemBase
    {
        public static PopulationCommand Latest;
        private CitySystem _citySystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _citySystem = World.GetOrCreateSystemManaged<CitySystem>();
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole != MultiplayerRole.Client)
            {
                Latest = null;
                return;
            }

            if (Latest == null)
            {
                return;
            }

            Entity city = _citySystem.City;
            if (city == Entity.Null || !EntityManager.HasComponent<Population>(city))
            {
                return;
            }

            Population population = EntityManager.GetComponentData<Population>(city);
            population.m_Population = Latest.Population;
            population.m_PopulationWithMoveIn = Latest.PopulationWithMoveIn;
            population.m_AverageHappiness = Latest.AverageHappiness;
            population.m_AverageHealth = Latest.AverageHealth;
            EntityManager.SetComponentData(city, population);
        }
    }
}
