using System.Collections.Generic;
using System.Reflection;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.UI;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Custom names of buildings, roads/streets and the city. Renaming only goes through NameSystem.SetCustomName
    ///     (and CityConfigurationSystem.cityName), which keeps the names in a dictionary, so that dictionary is
    ///     compared with the last known copy a few times a second, the same way as taxes and budgets.
    ///     District names aren't synced here, as districts have no position to identify them by; transport line
    ///     names go with the rest of the line settings (TransitLineSettingsSystem).
    /// </summary>
    public partial class NameSyncSystem : GameSystemBase
    {
        public static readonly InboundQueue<RenameCommand> Pending = new InboundQueue<RenameCommand>("NameSyncSystem", 2048, 64);

        private const float CheckInterval = 0.25f;

        private static readonly FieldInfo NamesField =
            typeof(NameSystem).GetField("m_Names", BindingFlags.Instance | BindingFlags.NonPublic);

        private NameSystem _nameSystem;
        private CityConfigurationSystem _cityConfigurationSystem;
        private PrefabSystem _prefabSystem;

        private Dictionary<Entity, string> _known;
        private string _knownCity;
        private float _lastCheck;

        protected override void OnCreate()
        {
            base.OnCreate();
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            _cityConfigurationSystem = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            if (NamesField == null)
            {
                BaseGameLog.Warn("NameSystem.m_Names not found: building and street names won't sync");
            }
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                _known = null;
                _knownCity = null;
                return;
            }

            while (Pending.TryDequeue(out RenameCommand command))
            {
                try
                {
                    Apply(command);
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(NameSyncSystem), e);
                }
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastCheck < CheckInterval)
            {
                return;
            }

            _lastCheck = now;
            Dictionary<Entity, string> names = GetNames();
            string city = _cityConfigurationSystem.cityName ?? "";
            if (_known != null && Command.SendToAll != null)
            {
                SendChanges(names, city);
            }

            _known = names;
            _knownCity = city;
        }

        private void SendChanges(Dictionary<Entity, string> names, string city)
        {
            foreach (KeyValuePair<Entity, string> pair in names)
            {
                if (!_known.TryGetValue(pair.Key, out string old) || old != pair.Value)
                {
                    Send(pair.Key, pair.Value);
                }
            }

            foreach (KeyValuePair<Entity, string> pair in _known)
            {
                // a removed name; a deleted entity drops its name too, which isn't a rename
                if (!names.ContainsKey(pair.Key) && EntityManager.Exists(pair.Key) &&
                    !EntityManager.HasComponent<Deleted>(pair.Key))
                {
                    Send(pair.Key, "");
                }
            }

            if (_knownCity != null && city != _knownCity)
            {
                Command.SendToAll(new RenameCommand { City = true, Name = city });
                BaseGameLog.Info($"City name sent: {city}");
            }
        }

        private void Send(Entity entity, string name)
        {
            if (!EntityManager.Exists(entity))
            {
                return;
            }

            // a street is selected (and named) as its Aggregate; describe it by its first segment
            bool street = false;
            Entity described = entity;
            if (EntityManager.HasBuffer<AggregateElement>(entity))
            {
                DynamicBuffer<AggregateElement> elements = EntityManager.GetBuffer<AggregateElement>(entity, true);
                if (elements.Length == 0)
                {
                    return;
                }

                described = elements[0].m_Edge;
                street = true;
            }

            // vehicles and creatures move, so a position can't find them on another game
            if (EntityManager.HasComponent<Game.Vehicles.Vehicle>(described) ||
                EntityManager.HasComponent<Game.Creatures.Creature>(described))
            {
                return;
            }

            NetRef target = NetRefs.Describe(EntityManager, described, _prefabSystem);
            if (target.Kind == 0)
            {
                return;
            }

            Command.SendToAll(new RenameCommand { Target = target, Street = street, Name = name });
            BaseGameLog.Info($"Name sent: \"{name}\" for {(street ? "street" : target.Prefab ?? "road")}");
        }

        private void Apply(RenameCommand command)
        {
            string name = command.Name ?? "";
            if (command.City)
            {
                _cityConfigurationSystem.cityName = name;
                _knownCity = name;
                BaseGameLog.Info($"City name received: {name}");
                return;
            }

            if (!NetRefs.Resolve(EntityManager, command.Target, out Entity entity, _prefabSystem) || entity == Entity.Null)
            {
                BaseGameLog.Warn($"Name received for something missing here (\"{name}\")");
                return;
            }

            if (command.Street)
            {
                if (!EntityManager.HasComponent<Aggregated>(entity))
                {
                    return;
                }

                entity = EntityManager.GetComponentData<Aggregated>(entity).m_Aggregate;
                if (entity == Entity.Null)
                {
                    return;
                }
            }

            _nameSystem.SetCustomName(entity, name);

            // already known, so it isn't sent back
            if (_known != null)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    _known.Remove(entity);
                }
                else
                {
                    _known[entity] = name;
                }
            }

            BaseGameLog.Info($"Name received: \"{name}\"");
        }

        private Dictionary<Entity, string> GetNames()
        {
            var names = NamesField?.GetValue(_nameSystem) as Dictionary<Entity, string>;
            return names != null ? new Dictionary<Entity, string>(names) : new Dictionary<Entity, string>();
        }
    }
}
