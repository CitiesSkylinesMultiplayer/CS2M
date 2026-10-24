using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Areas;
using Game.City;
using Game.Common;
using Game.Policies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using AreaNode = Game.Areas.Node;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Keeps city and district policies the same for everyone. The Policy buffers on the city and on each
    ///     district are compared with the last known state twice a second; a change is sent, a received one is
    ///     applied the way the policies panel does it (an Event + Modify entity, handled by ModifiedSystem).
    /// </summary>
    public partial class PolicySyncSystem : GameSystemBase
    {
        public static readonly InboundQueue<PolicyCommand> Pending = new InboundQueue<PolicyCommand>("PolicySyncSystem", 2048, 64);

        private const float CheckInterval = 0.5f;

        // a received change reaches the Policy buffer a frame or more later; until then the old value
        // must not be mistaken for a local change and sent back
        private const float ExpectTimeout = 3f;

        private struct State
        {
            public bool Active;
            public float Adjustment;

            public bool SameAs(State other) => Active == other.Active && (!Active || Adjustment == other.Adjustment);
        }

        private CitySystem _citySystem;
        private PrefabSystem _prefabSystem;
        private EntityQuery _districts;
        private EntityArchetype _modifyArchetype;

        private readonly Dictionary<Entity, Dictionary<string, State>> _known = new Dictionary<Entity, Dictionary<string, State>>();
        private readonly Dictionary<(Entity, string), float> _expected = new Dictionary<(Entity, string), float>();
        private float _lastCheck;

        protected override void OnCreate()
        {
            base.OnCreate();
            _citySystem = World.GetOrCreateSystemManaged<CitySystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _districts = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<District>(), ComponentType.ReadOnly<Policy>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
            // the same archetype PoliciesUISystem uses for setPolicy / setCityPolicy
            _modifyArchetype = EntityManager.CreateArchetype(ComponentType.ReadWrite<Event>(), ComponentType.ReadWrite<Modify>());
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                _known.Clear();
                _expected.Clear();
                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            while (Pending.TryDequeue(out PolicyCommand command))
            {
                try
                {
                    Apply(command, now);
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(PolicySyncSystem), e);
                }
            }

            if (now - _lastCheck < CheckInterval || Command.SendToAll == null)
            {
                return;
            }

            _lastCheck = now;
            var seen = new HashSet<Entity>();
            Entity city = _citySystem.City;
            if (city != Entity.Null && EntityManager.Exists(city) && EntityManager.HasBuffer<Policy>(city))
            {
                Check(city, null, now);
                seen.Add(city);
            }

            using (NativeArray<Entity> districts = _districts.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity district in districts)
                {
                    Check(district, AreaRefs.Key(EntityManager, _prefabSystem, district), now);
                    seen.Add(district);
                }
            }

            // forget removed districts (entity ids are reused)
            var gone = new List<Entity>();
            foreach (Entity entity in _known.Keys)
            {
                if (!seen.Contains(entity))
                {
                    gone.Add(entity);
                }
            }

            foreach (Entity entity in gone)
            {
                _known.Remove(entity);
            }
        }

        private void Apply(PolicyCommand command, float now)
        {
            Entity target;
            if (command.District == null)
            {
                target = _citySystem.City;
            }
            else
            {
                target = AreaRefs.Resolve(EntityManager, _prefabSystem, command.District);
            }

            Entity policy = PrefabLookup.Find(EntityManager, _prefabSystem, command.Policy);
            if (target == Entity.Null || policy == Entity.Null || !EntityManager.HasBuffer<Policy>(target))
            {
                BaseGameLog.Warn($"Policy received for a {(command.District == null ? "city" : "district")} or policy not found here: {command.Policy}");
                return;
            }

            var buffer = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = buffer.CreateEntity(_modifyArchetype);
            buffer.SetComponent(entity, new Modify(target, policy, command.Active, Inbound.PolicyAdjustment(EntityManager, policy, command.Adjustment)));
            buffer.Playback(EntityManager);
            buffer.Dispose();

            if (!_known.TryGetValue(target, out Dictionary<string, State> known))
            {
                known = Read(target);
                _known[target] = known;
            }

            known[command.Policy] = new State { Active = command.Active, Adjustment = command.Adjustment };
            _expected[(target, command.Policy)] = now + ExpectTimeout;
            BaseGameLog.Info($"Policy received: {command.Policy} {(command.Active ? "on" : "off")} ({command.Adjustment}) for {(command.District == null ? "the city" : "a district")}");
        }

        private void Check(Entity target, AreaKey district, float now)
        {
            Dictionary<string, State> current = Read(target);
            if (!_known.TryGetValue(target, out Dictionary<string, State> known))
            {
                // first look (just joined, or a new district): nothing to compare with
                _known[target] = current;
                return;
            }

            var names = new HashSet<string>(current.Keys);
            names.UnionWith(known.Keys);
            foreach (string name in names)
            {
                bool hasNow = current.TryGetValue(name, out State value);
                bool hadBefore = known.TryGetValue(name, out State before);
                State a = hasNow ? value : new State { Active = false, Adjustment = before.Adjustment };
                State b = hadBefore ? before : new State { Active = false, Adjustment = a.Adjustment };

                if (_expected.TryGetValue((target, name), out float until))
                {
                    if (a.SameAs(b) || now > until)
                    {
                        // arrived, or never will (e.g. the game refused it): either way not a local change
                        _expected.Remove((target, name));
                    }
                    else
                    {
                        // keep waiting for the received value to reach the local buffer
                        current[name] = b;
                    }

                    continue;
                }

                if (a.SameAs(b))
                {
                    continue;
                }

                Command.SendToAll(new PolicyCommand { District = district, Policy = name, Active = a.Active, Adjustment = a.Adjustment });
                BaseGameLog.Info($"Policy sent: {name} {(a.Active ? "on" : "off")} ({a.Adjustment}) for {(district == null ? "the city" : "a district")}");
            }

            _known[target] = current;
        }

        private Dictionary<string, State> Read(Entity target)
        {
            var result = new Dictionary<string, State>();
            DynamicBuffer<Policy> policies = EntityManager.GetBuffer<Policy>(target, true);
            foreach (Policy policy in policies)
            {
                string name = PrefabLookup.Name(_prefabSystem, policy.m_Policy);
                if (name != null)
                {
                    result[name] = new State { Active = (policy.m_Flags & PolicyFlags.Active) != 0, Adjustment = policy.m_Adjustment };
                }
            }

            return result;
        }
    }
}
