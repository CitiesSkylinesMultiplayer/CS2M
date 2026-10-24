using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Policies;
using Game.Prefabs;
using Game.Tools;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Settings changed from a building's info panel. Runs at the start of Modification1 (before ModifiedSystem in
    ///     Modification4 and before the game deletes anything as a consequence), because what the panel does only
    ///     lives for one MainLoop (played back by EndFrameBarrier, removed at Cleanup, before PreSimulation runs):
    ///     - Policies: the panel never touches the building; PoliciesUISystem creates a Game.Policies.Modify event
    ///       (out of service, emptying, building policies and their sliders, an upgrade's on/off). Those events are
    ///       sent, and received ones are created as the same event so ModifiedSystem applies them like a click.
    ///       Received events are made after the capture in the same update and are gone next frame, so they're
    ///       never sent back.
    ///     - Delete buttons (the building's, or an upgrade's in the Upgrades section) add Deleted directly instead
    ///       of going through the bulldozer, so the removed selection is sent as a bulldoze.
    /// </summary>
    public partial class BuildingSettingsSyncSystem : GameSystemBase
    {
        public static readonly InboundQueue<BuildingPolicyCommand> Pending = new InboundQueue<BuildingPolicyCommand>("BuildingSettingsSyncSystem", 2048, 64);

        private PrefabSystem _prefabSystem;
        private ToolSystem _toolSystem;
        private SelectedInfoUISystem _selectedInfoUISystem;
        private EntityQuery _modifyEvents;
        private EntityQuery _deletedObjects;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _selectedInfoUISystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            _modifyEvents = GetEntityQuery(ComponentType.ReadOnly<Event>(), ComponentType.ReadOnly<Modify>());
            _deletedObjects = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                Any = new[] { ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<Game.Buildings.ServiceUpgrade>() },
                None = new[] { ComponentType.ReadOnly<Temp>() }
            });
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            if (Command.SendToAll != null)
            {
                SendPolicyChanges();
                SendPanelDeletes();
            }

            ApplyReceived();
        }

        private void SendPolicyChanges()
        {
            if (_modifyEvents.IsEmptyIgnoreFilter)
            {
                return;
            }

            using (NativeArray<Modify> events = _modifyEvents.ToComponentDataArray<Modify>(Allocator.Temp))
            {
                foreach (Modify modify in events)
                {
                    // only buildings and their upgrades; city, district and route policies have no position
                    if (!IsObject(modify.m_Entity))
                    {
                        continue;
                    }

                    NetRef target = NetRefs.Describe(EntityManager, modify.m_Entity, _prefabSystem);
                    string policy = PrefabLookup.Name(_prefabSystem, modify.m_Policy);
                    if (target.Kind != 3 || policy == null)
                    {
                        continue;
                    }

                    bool active = (modify.m_Flags & PolicyFlags.Active) != 0;
                    Command.SendToAll(new BuildingPolicyCommand
                    {
                        Target = target, Policy = policy, Active = active, Adjustment = modify.m_Adjustment
                    });
                    BaseGameLog.Info($"Building policy sent: {policy} {(active ? "on" : "off")} ({modify.m_Adjustment}) on {target.Prefab}");
                }
            }
        }

        private void SendPanelDeletes()
        {
            // the bulldozer's removals are sent by BulldozeCaptureSystem; the info panel is only open with the default tool
            Entity selected = _selectedInfoUISystem.selectedEntity;
            if (selected == Entity.Null || !(_toolSystem.activeTool is DefaultToolSystem) || _deletedObjects.IsEmptyIgnoreFilter)
            {
                return;
            }

            var targets = new List<NetRef>();
            using (NativeArray<Entity> entities = _deletedObjects.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    bool upgradeOfSelected = EntityManager.HasComponent<Owner>(entity) &&
                                             EntityManager.GetComponentData<Owner>(entity).m_Owner == selected;
                    if (entity != selected && !upgradeOfSelected)
                    {
                        continue;
                    }

                    // the panel can't delete growables, so one deleted here was removed by the simulation
                    // (which is the host's call), not by this player
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    if (EntityManager.HasComponent<SpawnableBuildingData>(prefab) &&
                        !EntityManager.HasComponent<SignatureBuildingData>(prefab))
                    {
                        continue;
                    }

                    NetRef target = NetRefs.Describe(EntityManager, entity, _prefabSystem);
                    if (target.Kind == 3)
                    {
                        targets.Add(target);
                    }
                }
            }

            if (targets.Count > 0)
            {
                Command.SendToAll(new BulldozeCommand { Targets = targets.ToArray() });
                BaseGameLog.Info($"Info panel delete sent: {string.Join(", ", targets.ConvertAll(t => t.Prefab))}");
            }
        }

        private void ApplyReceived()
        {
            if (Pending.IsEmpty)
            {
                return;
            }

            var buffer = new EntityCommandBuffer(Allocator.Temp);
            while (Pending.TryDequeue(out BuildingPolicyCommand command))
            {
                try
                {
                    Entity policy = PrefabLookup.Find(EntityManager, _prefabSystem, command.Policy);
                    if (policy == Entity.Null ||
                        !NetRefs.Resolve(EntityManager, command.Target, out Entity target, _prefabSystem) || target == Entity.Null)
                    {
                        BaseGameLog.Warn($"Building policy received for something missing here ({command.Policy} on {command.Target?.Prefab})");
                        continue;
                    }

                    Entity modify = buffer.CreateEntity();
                    buffer.AddComponent<Event>(modify);
                    buffer.AddComponent(modify, new Modify(target, policy, command.Active, Inbound.PolicyAdjustment(EntityManager, policy, command.Adjustment)));
                    BaseGameLog.Info($"Building policy received: {command.Policy} {(command.Active ? "on" : "off")} on {command.Target.Prefab}");
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(BuildingSettingsSyncSystem), e);
                }
            }

            buffer.Playback(EntityManager);
            buffer.Dispose();
        }

        private bool IsObject(Entity entity)
        {
            return entity != Entity.Null && EntityManager.Exists(entity) &&
                   EntityManager.HasComponent<Game.Objects.Transform>(entity) && EntityManager.HasComponent<PrefabRef>(entity);
        }
    }
}
