using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     The host decides what the city's simulation does to buildings and money; joiners copy it.
    ///     On joiners the zone spawner and the upkeep system (level-ups, abandonment) are switched off.
    /// </summary>
    public partial class HostAuthoritySystem : GameSystemBase
    {
        public static readonly InboundQueue<CommandBase> Pending = new InboundQueue<CommandBase>("HostAuthoritySystem", 8192, 256);

        private const float MoneyInterval = 1f;
        private const int WatchFrames = 30;

        /// <summary>
        ///     The most one ChargeCommand may take. One tool action costs far less (the dearest buildings cost
        ///     a few million); a larger value is a bad or hostile command.
        /// </summary>
        private const long MaxCharge = 50000000;

        private ZoneSpawnSystem _zoneSpawnSystem;
        private BuildingUpkeepSystem _buildingUpkeepSystem;
        private CitySystem _citySystem;
        private PrefabSystem _prefabSystem;
        private EntityArchetype _spawnArchetype;

        private bool _disabledHere;
        private float _lastMoneySend;
        private int _frame;

        private EntityQuery _levelUps;
        private EntityQuery _abandoned;
        private readonly HashSet<Entity> _sentLevelUps = new HashSet<Entity>();
        private readonly HashSet<Entity> _sentAbandoned = new HashSet<Entity>();

        // host: what each joiner has been charged so far this session, net of refunds. A refund (a negative
        // charge, e.g. bulldozing something just built) is only given back out of that, so a joiner can never
        // add money to the city by sending negative charges.
        private readonly Dictionary<int, long> _charged = new Dictionary<int, long>();

        protected override void OnCreate()
        {
            base.OnCreate();
            _zoneSpawnSystem = World.GetOrCreateSystemManaged<ZoneSpawnSystem>();
            _buildingUpkeepSystem = World.GetOrCreateSystemManaged<BuildingUpkeepSystem>();
            _citySystem = World.GetOrCreateSystemManaged<CitySystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            // same archetype as ZoneSpawnSystem's definitions: created Deleted, built this frame
            _spawnArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<CreationDefinition>(),
                ComponentType.ReadWrite<ObjectDefinition>(),
                ComponentType.ReadWrite<Updated>(),
                ComponentType.ReadWrite<Deleted>());
            _levelUps = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<UnderConstruction>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
            _abandoned = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<Abandoned>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            MultiplayerRole role = Command.CurrentRole;
            SetSimulationDecisions(role != MultiplayerRole.Client);
            if (role == MultiplayerRole.None)
            {
                _sentLevelUps.Clear();
                _sentAbandoned.Clear();
                _charged.Clear();
                return;
            }

            if (role == MultiplayerRole.Server)
            {
                SendMoney();
                if (++_frame % WatchFrames == 0)
                {
                    WatchBuildings();
                }
            }

            while (Pending.TryDequeue(out CommandBase command))
            {
                try
                {
                    // the handlers already refuse these from the wrong side; the role checks here are a second layer
                    switch (command)
                    {
                        case SpawnCommand spawn when role == MultiplayerRole.Client:
                            ApplySpawn(spawn);
                            break;
                        case LevelUpCommand levelUp when role == MultiplayerRole.Client:
                            ApplyLevelUp(levelUp);
                            break;
                        case AbandonCommand abandon when role == MultiplayerRole.Client:
                            ApplyAbandon(abandon);
                            break;
                        case MoneyCommand money when role == MultiplayerRole.Client:
                            SetMoney(money.Money);
                            break;
                        case ChargeCommand charge when role == MultiplayerRole.Server:
                            ApplyCharge(charge);
                            break;
                    }
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(HostAuthoritySystem), e);
                }
            }
        }

        private void SetSimulationDecisions(bool enabled)
        {
            if (!enabled && !_disabledHere)
            {
                _zoneSpawnSystem.Enabled = false;
                _buildingUpkeepSystem.Enabled = false;
                _disabledHere = true;
                BaseGameLog.Info("Joined as a client: building spawns and upkeep now come from the host");
            }
            else if (enabled && _disabledHere)
            {
                _zoneSpawnSystem.Enabled = true;
                _buildingUpkeepSystem.Enabled = true;
                _disabledHere = false;
            }
        }

        // ---------- money ----------

        private int GetMoney()
        {
            Entity city = _citySystem.City;
            return city != Entity.Null && EntityManager.HasComponent<PlayerMoney>(city)
                ? EntityManager.GetComponentData<PlayerMoney>(city).money
                : 0;
        }

        private void ApplyCharge(ChargeCommand charge)
        {
            long amount = charge.Amount;
            if (amount > MaxCharge)
            {
                Inbound.Warn("charge-max", $"Charge of {amount} from player {charge.SenderId} is over the limit; {MaxCharge} charged");
                amount = MaxCharge;
            }

            _charged.TryGetValue(charge.SenderId, out long paid);
            if (amount < 0)
            {
                long refund = System.Math.Min(-amount, System.Math.Max(0L, paid));
                if (refund < -amount)
                {
                    Inbound.Warn("charge-refund",
                        $"Refund of {-amount} from player {charge.SenderId} is more than they have paid; {refund} refunded");
                }

                amount = -refund;
            }

            if (amount == 0)
            {
                return;
            }

            _charged[charge.SenderId] = paid + amount;
            SetMoney(ClampInt((long)GetMoney() - amount));
        }

        private static int ClampInt(long value) =>
            (int)System.Math.Max(int.MinValue, System.Math.Min(int.MaxValue, value));

        private void SetMoney(int amount)
        {
            Entity city = _citySystem.City;
            if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
            {
                return;
            }

            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
            // PlayerMoney keeps within +-2e9 but adds in int; step towards the target so nothing overflows
            const long limit = 2000000000;
            long target = System.Math.Max(-limit, System.Math.Min(limit, (long)amount));
            long difference = target - money.money;
            while (difference != 0)
            {
                int step = (int)System.Math.Max(-limit, System.Math.Min(limit, difference));
                money.Add(step);
                difference -= step;
            }

            EntityManager.SetComponentData(city, money);
        }

        private void SendMoney()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastMoneySend < MoneyInterval)
            {
                return;
            }

            _lastMoneySend = now;
            Command.SendToAll?.Invoke(new MoneyCommand { Money = GetMoney() });
            Entity city = _citySystem.City;
            if (city != Entity.Null && EntityManager.HasComponent<Population>(city))
            {
                Population population = EntityManager.GetComponentData<Population>(city);
                Command.SendToAll?.Invoke(new PopulationCommand
                {
                    Population = population.m_Population,
                    PopulationWithMoveIn = population.m_PopulationWithMoveIn,
                    AverageHappiness = population.m_AverageHappiness,
                    AverageHealth = population.m_AverageHealth
                });
            }
        }

        // ---------- level-ups and abandonment (host) ----------

        private void WatchBuildings()
        {
            using (NativeArray<Entity> entities = _levelUps.ToEntityArray(Allocator.Temp))
            {
                var current = new HashSet<Entity>();
                foreach (Entity entity in entities)
                {
                    UnderConstruction construction = EntityManager.GetComponentData<UnderConstruction>(entity);
                    if (construction.m_NewPrefab == Entity.Null)
                    {
                        continue;
                    }

                    current.Add(entity);
                    if (_sentLevelUps.Contains(entity))
                    {
                        continue;
                    }

                    NetRef building = NetRefs.Describe(EntityManager, entity, _prefabSystem);
                    string newPrefab = PrefabLookup.Name(_prefabSystem, construction.m_NewPrefab);
                    if (building.Kind == 3 && newPrefab != null)
                    {
                        Command.SendToAll?.Invoke(new LevelUpCommand
                        {
                            Building = building, NewPrefab = newPrefab,
                            Progress = construction.m_Progress, Speed = construction.m_Speed
                        });
                        BaseGameLog.Info($"Level-up sent: {building.Prefab} -> {newPrefab}");
                    }

                    _sentLevelUps.Add(entity);
                }

                _sentLevelUps.IntersectWith(current);
            }

            using (NativeArray<Entity> entities = _abandoned.ToEntityArray(Allocator.Temp))
            {
                var current = new HashSet<Entity>(entities.ToArray());
                foreach (Entity entity in entities)
                {
                    if (_sentAbandoned.Contains(entity))
                    {
                        continue;
                    }

                    NetRef building = NetRefs.Describe(EntityManager, entity, _prefabSystem);
                    if (building.Kind == 3)
                    {
                        Command.SendToAll?.Invoke(new AbandonCommand
                        {
                            Building = building,
                            AbandonmentTime = EntityManager.GetComponentData<Abandoned>(entity).m_AbandonmentTime
                        });
                    }

                    _sentAbandoned.Add(entity);
                }

                _sentAbandoned.IntersectWith(current);
            }
        }

        // ---------- applying the host's decisions (joiners) ----------

        private void ApplySpawn(SpawnCommand spawn)
        {
            Entity prefab = PrefabLookup.Find(EntityManager, _prefabSystem, spawn.Prefab);
            if (prefab == Entity.Null || !EntityManager.HasComponent<BuildingData>(prefab))
            {
                Inbound.Warn("spawn-prefab", $"Spawn received with an unknown prefab {spawn.Prefab}");
                return;
            }

            NetRefs.Resolve(EntityManager, spawn.Attached, out Entity attached, _prefabSystem);
            var definition = new CreationDefinition
            {
                m_Prefab = prefab,
                m_Attached = attached,
                m_Flags = (CreationFlags)spawn.CreationFlags,
                m_RandomSeed = spawn.RandomSeed
            };
            var objectDefinition = new ObjectDefinition
            {
                m_Position = NetRefs.V3(spawn.Position),
                m_LocalPosition = NetRefs.V3(spawn.LocalPosition),
                m_Scale = NetRefs.V3(spawn.Scale),
                m_Rotation = Definitions.Q(spawn.Rotation),
                m_LocalRotation = Definitions.Q(spawn.LocalRotation),
                m_Elevation = spawn.Elevation,
                m_Intensity = spawn.Intensity,
                m_Age = spawn.Age,
                m_IsDecoration = spawn.IsDecoration,
                m_ParentMesh = spawn.ParentMesh,
                m_GroupIndex = spawn.GroupIndex,
                m_Probability = spawn.Probability,
                m_PrefabSubIndex = spawn.PrefabSubIndex
            };

            var buffer = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = buffer.CreateEntity(_spawnArchetype);
            buffer.SetComponent(entity, definition);
            buffer.SetComponent(entity, objectDefinition);
            buffer.Playback(EntityManager);
            buffer.Dispose();
        }

        private void ApplyLevelUp(LevelUpCommand levelUp)
        {
            Entity newPrefab = PrefabLookup.Find(EntityManager, _prefabSystem, levelUp.NewPrefab);
            if (newPrefab == Entity.Null || !EntityManager.HasComponent<BuildingData>(newPrefab) ||
                !NetRefs.Resolve(EntityManager, levelUp.Building, out Entity building, _prefabSystem) ||
                building == Entity.Null || !EntityManager.HasComponent<Building>(building))
            {
                Inbound.Warn("levelup-missing", $"Level-up for a building that isn't here ({levelUp.Building?.Prefab})");
                return;
            }

            var construction = new UnderConstruction
            {
                m_NewPrefab = newPrefab, m_Progress = levelUp.Progress, m_Speed = levelUp.Speed
            };
            if (EntityManager.HasComponent<UnderConstruction>(building))
            {
                EntityManager.SetComponentData(building, construction);
            }
            else
            {
                EntityManager.AddComponentData(building, construction);
            }
        }

        private void ApplyAbandon(AbandonCommand abandon)
        {
            if (!NetRefs.Resolve(EntityManager, abandon.Building, out Entity building, _prefabSystem) ||
                building == Entity.Null || !EntityManager.HasComponent<Building>(building) ||
                EntityManager.HasComponent<Abandoned>(building))
            {
                return;
            }

            EntityManager.AddComponentData(building, new Abandoned { m_AbandonmentTime = abandon.AbandonmentTime });
            if (!EntityManager.HasComponent<Updated>(building))
            {
                EntityManager.AddComponent<Updated>(building);
            }
        }
    }
}
