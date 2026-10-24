using System.Collections.Generic;
using Colossal.Mathematics;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Marks that the road tool applied this frame (ApplyTool phase), so NetDiffCaptureSystem knows the
    ///     Created/Deleted roads after the tool output are this player's.
    /// </summary>
    public partial class NetApplyMarkerSystem : GameSystemBase
    {
        public static bool RoadToolApplied;
        private ToolSystem _toolSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        }

        protected override void OnUpdate()
        {
            if (_toolSystem.activeTool is NetToolSystem)
            {
                RoadToolApplied = true;
            }
        }
    }

    /// <summary>
    ///     Runs right after the ToolOutputBarrier, when the applied roads exist with Created/Deleted tags
    ///     (they're cleared at the end of the frame). Sends what changed, then re-sends the zoning of the
    ///     zone blocks along the new segments once the game has generated them.
    /// </summary>
    public partial class NetDiffCaptureSystem : GameSystemBase
    {
        private const int ZoneResyncFrames = 90;

        private PrefabSystem _prefabSystem;
        private EntityQuery _createdEdges;
        private EntityQuery _deletedEdges;
        private EntityQuery _deletedNodes;

        private readonly HashSet<Entity> _watchEdges = new HashSet<Entity>();
        private readonly HashSet<Entity> _resyncedBlocks = new HashSet<Entity>();
        private int _watchFrames;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _createdEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Created>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
            _deletedEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() }
            });
            _deletedNodes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() }
            });
        }

        protected override void OnUpdate()
        {
            bool applied = NetApplyMarkerSystem.RoadToolApplied;
            NetApplyMarkerSystem.RoadToolApplied = false;
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null)
            {
                _watchEdges.Clear();
                _resyncedBlocks.Clear();
                _watchFrames = 0;
                return;
            }

            if (applied)
            {
                SendDiff();
            }

            if (_watchFrames > 0)
            {
                _watchFrames--;
                ResyncZoning();
                if (_watchFrames == 0)
                {
                    _watchEdges.Clear();
                    _resyncedBlocks.Clear();
                }
            }
        }

        private void SendDiff()
        {
            var deletedEdges = new List<NetRef>();
            var deletedNodes = new List<NetRef>();
            var created = new List<EdgeData>();
            using (NativeArray<Entity> entities = _deletedEdges.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    deletedEdges.Add(NetRefs.Describe(EntityManager, entity));
                }
            }

            using (NativeArray<Entity> entities = _deletedNodes.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    deletedNodes.Add(NetRefs.Describe(EntityManager, entity));
                }
            }

            using (NativeArray<Entity> entities = _createdEdges.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    EdgeData data = DescribeEdge(entity);
                    if (data != null)
                    {
                        created.Add(data);
                        _watchEdges.Add(entity);
                    }
                }
            }

            if (created.Count == 0 && deletedEdges.Count == 0 && deletedNodes.Count == 0)
            {
                return;
            }

            Command.SendToAll(new NetDiffCommand
            {
                DeletedEdges = deletedEdges.ToArray(),
                DeletedNodes = deletedNodes.ToArray(),
                CreatedEdges = created.ToArray()
            });
            _watchFrames = ZoneResyncFrames;
            BaseGameLog.Info($"Roads sent: {created.Count} segment(s) created, {deletedEdges.Count} removed, {deletedNodes.Count} junction(s) removed");
        }

        private EdgeData DescribeEdge(Entity entity)
        {
            string prefab = PrefabLookup.Name(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
            if (prefab == null)
            {
                return null;
            }

            Edge edge = EntityManager.GetComponentData<Edge>(entity);
            Bezier4x3 b = EntityManager.GetComponentData<Curve>(entity).m_Bezier;
            return new EdgeData
            {
                Prefab = prefab,
                Curve = new[] { b.a.x, b.a.y, b.a.z, b.b.x, b.b.y, b.b.z, b.c.x, b.c.y, b.c.z, b.d.x, b.d.y, b.d.z },
                Elevation = Elev(entity),
                StartNode = NetRefs.F(EntityManager.GetComponentData<Node>(edge.m_Start).m_Position),
                EndNode = NetRefs.F(EntityManager.GetComponentData<Node>(edge.m_End).m_Position),
                StartElevation = Elev(edge.m_Start),
                EndElevation = Elev(edge.m_End)
            };
        }

        private float[] Elev(Entity entity)
        {
            float2 e = EntityManager.HasComponent<Game.Net.Elevation>(entity)
                ? EntityManager.GetComponentData<Game.Net.Elevation>(entity).m_Elevation
                : float2.zero;
            return new[] { e.x, e.y };
        }

        // Zoning on a split road lives on the new halves' blocks; the receiver rebuilt those from scratch
        private void ResyncZoning()
        {
            if (_watchEdges.Count == 0)
            {
                return;
            }

            // only the blocks of the watched segments, through their SubBlock buffers, not every block in the city
            foreach (Entity edge in _watchEdges)
            {
                if (!EntityManager.Exists(edge) || !EntityManager.HasBuffer<SubBlock>(edge))
                {
                    continue;
                }

                DynamicBuffer<SubBlock> subBlocks = EntityManager.GetBuffer<SubBlock>(edge, true);
                for (int b = 0; b < subBlocks.Length; b++)
                {
                    Entity entity = subBlocks[b].m_SubBlock;
                    if (_resyncedBlocks.Contains(entity) || !EntityManager.Exists(entity) ||
                        !EntityManager.HasComponent<Block>(entity) || !EntityManager.HasBuffer<Cell>(entity) ||
                        EntityManager.HasComponent<Temp>(entity) || EntityManager.HasComponent<Deleted>(entity))
                    {
                        continue;
                    }

                    _resyncedBlocks.Add(entity);
                    DynamicBuffer<Cell> cells = EntityManager.GetBuffer<Cell>(entity, true);
                    var indices = new List<int>();
                    var zones = new List<ushort>();
                    for (int i = 0; i < cells.Length; i++)
                    {
                        if (cells[i].m_Zone.m_Index != 0)
                        {
                            indices.Add(i);
                            zones.Add(cells[i].m_Zone.m_Index);
                        }
                    }

                    if (indices.Count == 0)
                    {
                        continue;
                    }

                    Block block = EntityManager.GetComponentData<Block>(entity);
                    Command.SendToAll(new ZoneCellsCommand
                    {
                        PosX = block.m_Position.x, PosY = block.m_Position.y, PosZ = block.m_Position.z,
                        DirX = block.m_Direction.x, DirY = block.m_Direction.y,
                        SizeX = block.m_Size.x, SizeY = block.m_Size.y,
                        Cells = indices.ToArray(), Zones = zones.ToArray()
                    });
                }
            }
        }
    }

    /// <summary>
    ///     Applies another player's road changes: removes the segments/junctions they removed, then builds
    ///     the created segments as permanent definitions whose ends share exact positions, so the game merges
    ///     them into the same junctions (and reuses existing junctions at those positions).
    /// </summary>
    public partial class NetDiffApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<NetDiffCommand> Pending = new InboundQueue<NetDiffCommand>("NetDiffApplySystem", 1024, 8);

        private PrefabSystem _prefabSystem;
        private EntityArchetype _archetype;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _archetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<CreationDefinition>(),
                ComponentType.ReadWrite<NetCourse>(),
                ComponentType.ReadWrite<Updated>(),
                ComponentType.ReadWrite<Deleted>());
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            while (Pending.TryDequeue(out NetDiffCommand command))
            {
                try
                {
                    var removed = new HashSet<Entity>();
                    int missing = 0;
                    foreach (NetRef netRef in command.DeletedEdges ?? new NetRef[0])
                    {
                        if (NetRefs.Resolve(EntityManager, netRef, out Entity edge) && edge != Entity.Null)
                        {
                            removed.Add(edge);
                        }
                        else
                        {
                            missing++;
                        }
                    }

                    foreach (NetRef netRef in command.DeletedNodes ?? new NetRef[0])
                    {
                        if (NetRefs.Resolve(EntityManager, netRef, out Entity node) && node != Entity.Null)
                        {
                            removed.Add(node);
                        }
                    }

                    foreach (Entity entity in removed)
                    {
                        EntityManager.AddComponent<Deleted>(entity);
                    }

                    var buffer = new EntityCommandBuffer(Allocator.Temp);
                    int built = 0;
                    foreach (EdgeData data in command.CreatedEdges ?? new EdgeData[0])
                    {
                        Entity prefab = PrefabLookup.Find(EntityManager, _prefabSystem, data.Prefab);
                        if (prefab == Entity.Null || !EntityManager.HasComponent<NetData>(prefab))
                        {
                            BaseGameLog.Warn($"Road received with an unknown prefab {data.Prefab}");
                            continue;
                        }

                        float[] c = data.Curve;
                        var curve = new Bezier4x3(new float3(c[0], c[1], c[2]), new float3(c[3], c[4], c[5]),
                            new float3(c[6], c[7], c[8]), new float3(c[9], c[10], c[11]));
                        Entity entity = buffer.CreateEntity(_archetype);
                        buffer.SetComponent(entity, new CreationDefinition
                        {
                            m_Prefab = prefab,
                            m_Flags = CreationFlags.Permanent
                        });
                        buffer.SetComponent(entity, new NetCourse
                        {
                            m_Curve = curve,
                            m_Elevation = new float2(data.Elevation[0], data.Elevation[1]),
                            m_Length = MathUtils.Length(curve),
                            m_FixedIndex = -1,
                            m_StartPosition = End(NetRefs.V3(data.StartNode), MathUtils.StartTangent(curve), data.StartElevation, 0f, CoursePosFlags.IsFirst, removed),
                            m_EndPosition = End(NetRefs.V3(data.EndNode), MathUtils.EndTangent(curve), data.EndElevation, 1f, CoursePosFlags.IsLast, removed)
                        });
                        built++;
                    }

                    buffer.Playback(EntityManager);
                    buffer.Dispose();
                    BaseGameLog.Info($"Roads received: {built} segment(s) built, {removed.Count} removed, {missing} to remove not found here");
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(NetDiffApplySystem), e);
                }
            }
        }

        private CoursePos End(float3 position, float3 tangent, float[] elevation, float delta, CoursePosFlags flags, HashSet<Entity> removed)
        {
            // an existing junction at this spot is reused; otherwise the game makes one (shared by every
            // segment that ends at exactly this position)
            NetRefs.Resolve(EntityManager, new NetRef { Kind = 1, A = NetRefs.F(position) }, out Entity node);
            if (removed.Contains(node))
            {
                node = Entity.Null;
            }

            return new CoursePos
            {
                m_Entity = node,
                m_Position = position,
                m_Rotation = NetUtils.GetNodeRotation(tangent),
                m_Elevation = new float2(elevation[0], elevation[1]),
                m_CourseDelta = delta,
                m_Flags = flags,
                m_ParentMesh = -1
            };
        }
    }
}
