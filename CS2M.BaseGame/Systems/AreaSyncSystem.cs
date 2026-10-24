using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using AreaDataCmd = CS2M.BaseGame.Commands.AreaData;
using AreaNode = Game.Areas.Node;
using SubArea = Game.Areas.SubArea;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     ApplyTool phase, before ApplyAreasSystem: marks that the area tool or bulldozer applied this frame and
    ///     remembers the outline of every area about to be reshaped (ApplyAreasSystem overwrites the original's
    ///     nodes, and the other game still knows the area by its old outline).
    /// </summary>
    public partial class AreaApplyMarkerSystem : GameSystemBase
    {
        public static bool Applied;
        public static bool ByBulldozer;
        public static readonly Dictionary<Entity, AreaKey> OldOutlines = new Dictionary<Entity, AreaKey>();

        private ToolSystem _toolSystem;
        private PrefabSystem _prefabSystem;
        private EntityQuery _temps;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _temps = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Area>(), ComponentType.ReadOnly<AreaNode>());
        }

        protected override void OnUpdate()
        {
            bool areaTool = _toolSystem.activeTool is AreaToolSystem;
            bool bulldozer = _toolSystem.activeTool is BulldozeToolSystem;
            if (Command.CurrentRole == MultiplayerRole.None || (!areaTool && !bulldozer))
            {
                return;
            }

            Applied = true;
            ByBulldozer = bulldozer;
            OldOutlines.Clear();
            using (NativeArray<Temp> temps = _temps.ToComponentDataArray<Temp>(Allocator.Temp))
            {
                foreach (Temp temp in temps)
                {
                    if ((temp.m_Flags & TempFlags.Delete) != 0 || temp.m_Original == Entity.Null ||
                        !EntityManager.Exists(temp.m_Original) || !EntityManager.HasBuffer<AreaNode>(temp.m_Original))
                    {
                        continue;
                    }

                    AreaKey key = AreaRefs.Key(EntityManager, _prefabSystem, temp.m_Original);
                    if (key != null)
                    {
                        OldOutlines[temp.m_Original] = key;
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Runs right after the ToolOutputBarrier, when ApplyAreasSystem's result exists: new areas tagged
    ///     Created + Applied, reshaped originals tagged Updated with their new nodes, removed ones tagged Deleted.
    ///     Sends that result, so the other game reproduces exactly what was applied here.
    /// </summary>
    public partial class AreaCaptureSystem : GameSystemBase
    {
        private PrefabSystem _prefabSystem;
        private EntityQuery _changed;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _changed = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Area>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() }
            });
        }

        protected override void OnUpdate()
        {
            bool applied = AreaApplyMarkerSystem.Applied;
            AreaApplyMarkerSystem.Applied = false;
            if (!applied || Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null)
            {
                AreaApplyMarkerSystem.OldOutlines.Clear();
                AreaApplySystem.Received.Clear();
                return;
            }

            try
            {
                Capture();
            }
            finally
            {
                AreaApplyMarkerSystem.OldOutlines.Clear();
                AreaApplySystem.Received.Clear();
            }
        }

        private void Capture()
        {
            bool bulldozer = AreaApplyMarkerSystem.ByBulldozer;
            var deleted = new List<AreaKey>();
            var updated = new List<AreaUpdate>();
            var created = new List<AreaDataCmd>();
            using (NativeArray<Entity> entities = _changed.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    // map tiles are bought, not drawn; areas owned by another area are generated from it.
                    // Received changes are applied in the previous frame's PreSimulation and keep their
                    // Deleted/Updated tags until this frame's cleanup, so they must not be sent back.
                    if (EntityManager.HasComponent<MapTile>(entity) || AreaApplySystem.Received.Contains(entity))
                    {
                        continue;
                    }

                    Entity owner = EntityManager.HasComponent<Owner>(entity) ? EntityManager.GetComponentData<Owner>(entity).m_Owner : Entity.Null;
                    if (owner != Entity.Null && EntityManager.Exists(owner) && EntityManager.HasComponent<Area>(owner))
                    {
                        continue;
                    }

                    // a building created in this same apply (a landfill's lot): its areas went with its placement
                    if (owner != Entity.Null && EntityManager.Exists(owner) && EntityManager.HasComponent<Created>(owner) &&
                        !EntityManager.HasComponent<Area>(owner))
                    {
                        continue;
                    }

                    bool isDeleted = EntityManager.HasComponent<Deleted>(entity);
                    bool isCreated = EntityManager.HasComponent<Created>(entity);
                    if (isDeleted)
                    {
                        // removed together with its building or road: the other game removes it the same way
                        bool ownerGone = owner != Entity.Null && (!EntityManager.Exists(owner) || EntityManager.HasComponent<Deleted>(owner));
                        if (!isCreated && !ownerGone)
                        {
                            AreaKey key = AreaRefs.Key(EntityManager, _prefabSystem, entity);
                            if (key != null)
                            {
                                deleted.Add(key);
                            }
                        }

                        continue;
                    }

                    if (bulldozer)
                    {
                        continue;
                    }

                    if (isCreated)
                    {
                        // a building sent from the result this frame took its new areas with it
                        if (LotBuildingCaptureSystem.SentThisFrame)
                        {
                            continue;
                        }

                        if (EntityManager.HasComponent<Applied>(entity))
                        {
                            AreaDataCmd data = Describe(entity, owner);
                            if (data != null)
                            {
                                created.Add(data);
                            }
                        }

                        continue;
                    }

                    if (AreaApplyMarkerSystem.OldOutlines.TryGetValue(entity, out AreaKey old))
                    {
                        AreaDataCmd data = Describe(entity, owner);
                        if (data != null && !AreaRefs.Same(old.Nodes, AreaRefs.Positions(EntityManager.GetBuffer<AreaNode>(entity, true))))
                        {
                            updated.Add(new AreaUpdate { Old = old, New = data });
                        }
                    }
                }
            }

            if (deleted.Count == 0 && updated.Count == 0 && created.Count == 0)
            {
                return;
            }

            Command.SendToAll(new AreaChangeCommand
            {
                Deleted = deleted.ToArray(),
                Updated = updated.ToArray(),
                Created = created.ToArray()
            });
            BaseGameLog.Info($"Areas sent: {created.Count} created, {updated.Count} reshaped, {deleted.Count} removed");
        }

        private AreaDataCmd Describe(Entity entity, Entity owner)
        {
            string prefab = PrefabLookup.Name(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
            if (prefab == null)
            {
                return null;
            }

            DynamicBuffer<AreaNode> nodes = EntityManager.GetBuffer<AreaNode>(entity, true);
            var packed = new float[nodes.Length * 4];
            for (int i = 0; i < nodes.Length; i++)
            {
                packed[i * 4] = nodes[i].m_Position.x;
                packed[i * 4 + 1] = nodes[i].m_Position.y;
                packed[i * 4 + 2] = nodes[i].m_Position.z;
                packed[i * 4 + 3] = nodes[i].m_Elevation;
            }

            NetRef ownerRef = null;
            if (owner != Entity.Null)
            {
                // without its building the other game would get a loose extractor/lot area
                ownerRef = NetRefs.Describe(EntityManager, owner, _prefabSystem);
                if (ownerRef.Kind == 0)
                {
                    BaseGameLog.Warn($"Area {prefab} not sent: its owner can't be described");
                    return null;
                }
            }

            var data = new AreaDataCmd
            {
                Prefab = prefab,
                Nodes = packed,
                Complete = (EntityManager.GetComponentData<Area>(entity).m_Flags & AreaFlags.Complete) != 0,
                Owner = ownerRef,
                Seed = EntityManager.HasComponent<PseudoRandomSeed>(entity) ? EntityManager.GetComponentData<PseudoRandomSeed>(entity).m_Seed : 0
            };

            if (EntityManager.HasBuffer<LocalNodeCache>(entity))
            {
                DynamicBuffer<LocalNodeCache> cache = EntityManager.GetBuffer<LocalNodeCache>(entity, true);
                data.Cache = new float[cache.Length * 3];
                data.CacheMesh = new int[cache.Length];
                for (int i = 0; i < cache.Length; i++)
                {
                    data.Cache[i * 3] = cache[i].m_Position.x;
                    data.Cache[i * 3 + 1] = cache[i].m_Position.y;
                    data.Cache[i * 3 + 2] = cache[i].m_Position.z;
                    data.CacheMesh[i] = cache[i].m_ParentMesh;
                }
            }

            return data;
        }
    }

    /// <summary>
    ///     Applies another player's area changes. Removed areas get Deleted; reshaped ones get their new nodes
    ///     and Updated (what ApplyAreasSystem does to an original); new ones are built from permanent
    ///     CreationDefinitions with a node buffer, which GenerateAreasSystem turns into areas this frame.
    /// </summary>
    public partial class AreaApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<AreaChangeCommand> Pending = new InboundQueue<AreaChangeCommand>("AreaApplySystem", 1024, 16);

        // areas this system tagged Deleted/Updated, skipped (and cleared) by the next AreaCaptureSystem run
        public static readonly HashSet<Entity> Received = new HashSet<Entity>();

        private PrefabSystem _prefabSystem;
        private EntityArchetype _archetype;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _archetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<CreationDefinition>(),
                ComponentType.ReadWrite<Updated>(),
                ComponentType.ReadWrite<Deleted>());
        }

        protected override void OnUpdate()
        {
            // received while joining: wait until the city has loaded
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            while (Pending.TryDequeue(out AreaChangeCommand command))
            {
                try
                {
                    int removed = 0, reshaped = 0, built = 0, missing = 0;
                    var owners = new HashSet<Entity>();

                    foreach (AreaKey key in command.Deleted ?? new AreaKey[0])
                    {
                        Entity area = AreaRefs.Resolve(EntityManager, _prefabSystem, key);
                        if (area == Entity.Null)
                        {
                            missing++;
                            continue;
                        }

                        AddOwner(area, owners);
                        Delete(area);
                        removed++;
                    }

                    foreach (AreaUpdate update in command.Updated ?? new AreaUpdate[0])
                    {
                        Entity area = AreaRefs.Resolve(EntityManager, _prefabSystem, update.Old);
                        if (area == Entity.Null || update.New?.Nodes == null)
                        {
                            missing++;
                            continue;
                        }

                        DynamicBuffer<AreaNode> nodes = EntityManager.GetBuffer<AreaNode>(area);
                        nodes.Clear();
                        foreach (AreaNode node in Nodes(update.New))
                        {
                            nodes.Add(node);
                        }

                        SetCache(area, update.New);
                        MarkUpdated(area);

                        // generated sub-areas (Slave) copy their owner's outline only when they are Updated
                        // themselves (GeometrySystem); on the sender ApplyAreasSystem updates them via their Temps
                        foreach (Entity child in SubAreas(area))
                        {
                            if (EntityManager.Exists(child) && EntityManager.HasComponent<Area>(child) &&
                                (EntityManager.GetComponentData<Area>(child).m_Flags & AreaFlags.Slave) != 0)
                            {
                                MarkUpdated(child);
                            }
                        }

                        AddOwner(area, owners);
                        reshaped++;
                    }

                    var buffer = new EntityCommandBuffer(Allocator.Temp);
                    foreach (AreaDataCmd data in command.Created ?? new AreaDataCmd[0])
                    {
                        Entity prefab = PrefabLookup.Find(EntityManager, _prefabSystem, data.Prefab);
                        if (prefab == Entity.Null || data.Nodes == null)
                        {
                            BaseGameLog.Warn(prefab == Entity.Null
                                ? $"Area received with an unknown prefab {data.Prefab}"
                                : $"Area received without nodes ({data.Prefab})");
                            continue;
                        }

                        Entity owner = Entity.Null;
                        if (data.Owner != null && data.Owner.Kind != 0 &&
                            (!NetRefs.Resolve(EntityManager, data.Owner, out owner, _prefabSystem) || owner == Entity.Null))
                        {
                            BaseGameLog.Warn($"Area {data.Prefab} received, but its building was not found here");
                            missing++;
                            continue;
                        }

                        Entity entity = buffer.CreateEntity(_archetype);
                        buffer.SetComponent(entity, new CreationDefinition
                        {
                            m_Prefab = prefab,
                            m_Owner = owner,
                            m_Flags = CreationFlags.Permanent,
                            m_RandomSeed = data.Seed
                        });

                        // GenerateAreasSystem only marks a new area Complete (a closed shape) when its first node
                        // is repeated at the end, which is how the area tool hands over a finished outline
                        List<AreaNode> list = Nodes(data);
                        if (data.Complete && list.Count >= 3)
                        {
                            list.Add(list[0]);
                        }

                        DynamicBuffer<AreaNode> nodes = buffer.AddBuffer<AreaNode>(entity);
                        foreach (AreaNode node in list)
                        {
                            nodes.Add(node);
                        }

                        if (data.Cache != null && data.CacheMesh != null)
                        {
                            DynamicBuffer<LocalNodeCache> cache = buffer.AddBuffer<LocalNodeCache>(entity);
                            foreach (LocalNodeCache item in Cache(data))
                            {
                                cache.Add(item);
                            }

                            if (data.Complete && data.CacheMesh.Length >= 3 && cache.Length > 0)
                            {
                                cache.Add(cache[0]);
                            }
                        }

                        if (owner != Entity.Null)
                        {
                            owners.Add(owner);
                        }

                        built++;
                    }

                    buffer.Playback(EntityManager);
                    buffer.Dispose();

                    // the area tool upgrades the building that owns an extractor/lot area, so its stats refresh
                    foreach (Entity owner in owners)
                    {
                        if (EntityManager.Exists(owner) && !EntityManager.HasComponent<Deleted>(owner) &&
                            !EntityManager.HasComponent<Updated>(owner))
                        {
                            EntityManager.AddComponent<Updated>(owner);
                        }
                    }

                    BaseGameLog.Info($"Areas received: {built} created, {reshaped} reshaped, {removed} removed, {missing} not found here");
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(AreaApplySystem), e);
                }
            }
        }

        private void MarkUpdated(Entity area)
        {
            Received.Add(area);
            if (!EntityManager.HasComponent<Updated>(area))
            {
                EntityManager.AddComponent<Updated>(area);
            }
        }

        private void AddOwner(Entity area, HashSet<Entity> owners)
        {
            if (EntityManager.HasComponent<Owner>(area))
            {
                Entity owner = EntityManager.GetComponentData<Owner>(area).m_Owner;
                if (owner != Entity.Null && EntityManager.Exists(owner) && !EntityManager.HasComponent<Area>(owner))
                {
                    owners.Add(owner);
                }
            }
        }

        // the bulldozer removes an area's generated sub-areas with it
        private void Delete(Entity area)
        {
            if (EntityManager.HasComponent<Deleted>(area))
            {
                return;
            }

            foreach (Entity child in SubAreas(area))
            {
                if (EntityManager.Exists(child))
                {
                    Delete(child);
                }
            }

            Received.Add(area);
            EntityManager.AddComponent<Deleted>(area);
        }

        // copied out first: marking or deleting a child changes its structure and invalidates the buffer
        private List<Entity> SubAreas(Entity area)
        {
            var children = new List<Entity>();
            if (EntityManager.HasBuffer<SubArea>(area))
            {
                foreach (SubArea subArea in EntityManager.GetBuffer<SubArea>(area, true))
                {
                    children.Add(subArea.m_Area);
                }
            }

            return children;
        }

        private void SetCache(Entity area, AreaDataCmd data)
        {
            if (data.Cache == null || data.CacheMesh == null)
            {
                if (EntityManager.HasBuffer<LocalNodeCache>(area))
                {
                    EntityManager.RemoveComponent<LocalNodeCache>(area);
                }

                return;
            }

            DynamicBuffer<LocalNodeCache> cache = EntityManager.HasBuffer<LocalNodeCache>(area)
                ? EntityManager.GetBuffer<LocalNodeCache>(area)
                : EntityManager.AddBuffer<LocalNodeCache>(area);
            cache.Clear();
            foreach (LocalNodeCache item in Cache(data))
            {
                cache.Add(item);
            }
        }

        private static List<AreaNode> Nodes(AreaDataCmd data)
        {
            var list = new List<AreaNode>();
            for (int i = 0; i + 3 < data.Nodes.Length; i += 4)
            {
                list.Add(new AreaNode(new float3(data.Nodes[i], data.Nodes[i + 1], data.Nodes[i + 2]), data.Nodes[i + 3]));
            }

            return list;
        }

        private static List<LocalNodeCache> Cache(AreaDataCmd data)
        {
            var list = new List<LocalNodeCache>();
            for (int i = 0; i < data.CacheMesh.Length && i * 3 + 2 < data.Cache.Length; i++)
            {
                list.Add(new LocalNodeCache
                {
                    m_Position = new float3(data.Cache[i * 3], data.Cache[i * 3 + 1], data.Cache[i * 3 + 2]),
                    m_ParentMesh = data.CacheMesh[i]
                });
            }

            return list;
        }
    }
}
