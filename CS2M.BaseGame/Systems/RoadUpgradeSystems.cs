using System.Collections.Generic;
using Colossal.Mathematics;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using RoadFlags = Game.Net.RoadFlags;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     What RoadUpgradeCaptureSystem compares to find in-place changes to a road segment or junction.
    /// </summary>
    internal struct NetUpgradeState
    {
        public Entity Prefab;
        public bool HasUpgrades;
        public CompositionFlags Flags;
        public Entity StartNode;
        public string SubReplacements;

        // Where the segment/junction was before the change. The receiving games still have it there, and an
        // upgrade can move it (elevation or width changes), so it must be looked up by this, not the new geometry.
        public CS2M.BaseGame.Commands.NetRef Ref;

        public bool SameAs(NetUpgradeState other)
        {
            return Prefab == other.Prefab && HasUpgrades == other.HasUpgrades && Flags == other.Flags &&
                   StartNode == other.StartNode && SubReplacements == other.SubReplacements;
        }

        public static NetUpgradeState Read(EntityManager em, Entity entity)
        {
            var state = new NetUpgradeState { Prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab };
            if (em.HasComponent<Upgraded>(entity))
            {
                state.HasUpgrades = true;
                state.Flags = em.GetComponentData<Upgraded>(entity).m_Flags;
            }

            if (em.HasComponent<Edge>(entity))
            {
                state.StartNode = em.GetComponentData<Edge>(entity).m_Start;
            }

            if (em.HasBuffer<SubReplacement>(entity))
            {
                DynamicBuffer<SubReplacement> buffer = em.GetBuffer<SubReplacement>(entity, true);
                var parts = new List<string>();
                for (int i = 0; i < buffer.Length; i++)
                {
                    SubReplacement s = buffer[i];
                    parts.Add($"{s.m_Prefab.Index}:{s.m_Prefab.Version}:{(byte)s.m_Type}:{(sbyte)s.m_Side}:{(byte)s.m_AgeMask}");
                }

                state.SubReplacements = string.Join(";", parts);
            }

            return state;
        }
    }

    /// <summary>
    ///     ApplyTool phase, before ApplyNetSystem: remembers the state of every existing segment and junction
    ///     the road tool is about to update in place (Temp copies with an original that isn't deleted or
    ///     replaced). Replaced ones are re-created, which NetDiffCaptureSystem already sends.
    /// </summary>
    public partial class RoadUpgradeMarkerSystem : GameSystemBase
    {
        internal static readonly Dictionary<Entity, NetUpgradeState> Before = new Dictionary<Entity, NetUpgradeState>();

        private const TempFlags Skip = TempFlags.Delete | TempFlags.Replace | TempFlags.Combine | TempFlags.Cancel;

        private ToolSystem _toolSystem;
        private EntityQuery _temps;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _temps = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Node>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            Before.Clear();
            // Upgrades and road replacement use the road tool; street-tree types are placed with the object
            // tool, which upgrades the segments through the same Temp copies
            if (Command.CurrentRole == MultiplayerRole.None ||
                !(PlaceCaptureSystem.ApplyingTool(_toolSystem) is NetToolSystem || PlaceCaptureSystem.ApplyingTool(_toolSystem) is ObjectToolSystem))
            {
                return;
            }

            using (NativeArray<Entity> entities = _temps.ToEntityArray(Allocator.Temp))
            using (NativeArray<Temp> temps = _temps.ToComponentDataArray<Temp>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity original = temps[i].m_Original;
                    // a building's own roads come with the building
                    if (original == Entity.Null || (temps[i].m_Flags & Skip) != 0 || !EntityManager.Exists(original) ||
                        EntityManager.HasComponent<Temp>(original) || EntityManager.HasComponent<Owner>(original) ||
                        !EntityManager.HasComponent<PrefabRef>(original) || Before.ContainsKey(original))
                    {
                        continue;
                    }

                    NetUpgradeState before = NetUpgradeState.Read(EntityManager, original);
                    before.Ref = NetRefs.Describe(EntityManager, original);
                    Before[original] = before;
                }
            }
        }
    }

    /// <summary>
    ///     Runs right after the ToolOutputBarrier, when ApplyNetSystem has written the Temp copies back onto
    ///     the originals. Sends every remembered segment/junction whose prefab, upgrades, direction or street
    ///     trees changed (untouched neighbours the tool also copied are left out).
    /// </summary>
    public partial class RoadUpgradeCaptureSystem : GameSystemBase
    {
        private const RoadFlags AlignmentFlags = RoadFlags.StartHalfAligned | RoadFlags.EndHalfAligned;

        private PrefabSystem _prefabSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        protected override void OnUpdate()
        {
            if (RoadUpgradeMarkerSystem.Before.Count == 0)
            {
                return;
            }

            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null)
            {
                RoadUpgradeMarkerSystem.Before.Clear();
                return;
            }

            var edges = new List<EdgeUpgradeData>();
            var nodes = new List<NodeUpgradeData>();
            foreach (KeyValuePair<Entity, NetUpgradeState> pair in RoadUpgradeMarkerSystem.Before)
            {
                Entity entity = pair.Key;
                if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity) ||
                    NetUpgradeState.Read(EntityManager, entity).SameAs(pair.Value))
                {
                    continue;
                }

                if (EntityManager.HasComponent<Edge>(entity))
                {
                    EdgeUpgradeData data = DescribeEdge(entity, pair.Value.Ref);
                    if (data != null)
                    {
                        edges.Add(data);
                    }
                }
                else if (EntityManager.HasComponent<Node>(entity))
                {
                    string prefab = PrefabLookup.Name(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
                    if (prefab == null)
                    {
                        continue;
                    }

                    var data = new NodeUpgradeData { Node = pair.Value.Ref ?? NetRefs.Describe(EntityManager, entity), Prefab = prefab };
                    ReadUpgrades(entity, out bool has, out uint general, out uint left, out uint right);
                    data.HasUpgrades = has;
                    data.General = general;
                    data.Left = left;
                    data.Right = right;
                    nodes.Add(data);
                }
            }

            RoadUpgradeMarkerSystem.Before.Clear();
            if (edges.Count == 0 && nodes.Count == 0)
            {
                return;
            }

            Command.SendToAll(new RoadUpgradeCommand { Edges = edges.ToArray(), Nodes = nodes.ToArray() });
            BaseGameLog.Info($"Road upgrades sent: {edges.Count} segment(s), {nodes.Count} junction(s) changed in place");
        }

        private EdgeUpgradeData DescribeEdge(Entity entity, CS2M.BaseGame.Commands.NetRef before)
        {
            string prefab = PrefabLookup.Name(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
            if (prefab == null || !EntityManager.HasComponent<Curve>(entity))
            {
                return null;
            }

            Edge edge = EntityManager.GetComponentData<Edge>(entity);
            Bezier4x3 b = EntityManager.GetComponentData<Curve>(entity).m_Bezier;
            var data = new EdgeUpgradeData
            {
                Edge = before ?? NetRefs.Describe(EntityManager, entity),
                Prefab = prefab,
                Curve = new[] { b.a.x, b.a.y, b.a.z, b.b.x, b.b.y, b.b.z, b.c.x, b.c.y, b.c.z, b.d.x, b.d.y, b.d.z },
                StartNode = NetRefs.F(EntityManager.GetComponentData<Node>(edge.m_Start).m_Position),
                TramTrack = EntityManager.HasComponent<TramTrack>(entity),
                RoadAlignment = EntityManager.HasComponent<Road>(entity)
                    ? (int)(EntityManager.GetComponentData<Road>(entity).m_Flags & AlignmentFlags)
                    : -1
            };
            ReadUpgrades(entity, out bool has, out uint general, out uint left, out uint right);
            data.HasUpgrades = has;
            data.General = general;
            data.Left = left;
            data.Right = right;

            if (EntityManager.HasComponent<Game.Net.Elevation>(entity))
            {
                float2 e = EntityManager.GetComponentData<Game.Net.Elevation>(entity).m_Elevation;
                data.Elevation = new[] { e.x, e.y };
            }

            var subs = new List<SubReplacementData>();
            if (EntityManager.HasBuffer<SubReplacement>(entity))
            {
                DynamicBuffer<SubReplacement> buffer = EntityManager.GetBuffer<SubReplacement>(entity, true);
                for (int i = 0; i < buffer.Length; i++)
                {
                    SubReplacement s = buffer[i];
                    string tree = PrefabLookup.Name(_prefabSystem, s.m_Prefab);
                    if (tree != null)
                    {
                        subs.Add(new SubReplacementData { Prefab = tree, Type = (byte)s.m_Type, Side = (sbyte)s.m_Side, AgeMask = (byte)s.m_AgeMask });
                    }
                }
            }

            data.SubReplacements = subs.ToArray();
            return data;
        }

        private void ReadUpgrades(Entity entity, out bool has, out uint general, out uint left, out uint right)
        {
            has = EntityManager.HasComponent<Upgraded>(entity);
            CompositionFlags flags = has ? EntityManager.GetComponentData<Upgraded>(entity).m_Flags : default;
            general = (uint)flags.m_General;
            left = (uint)flags.m_Left;
            right = (uint)flags.m_Right;
        }
    }

    /// <summary>
    ///     Applies another player's in-place road changes the way ApplyNetSystem writes a Temp copy back onto
    ///     its original: set PrefabRef / Upgraded / Edge / Curve / Elevation / SubReplacement and tag the
    ///     segment, its junctions and their other segments Updated, so the game reselects the composition and
    ///     regenerates geometry, lanes, street trees and zone blocks. Segments that aren't here yet (built a
    ///     moment ago by a road command still being generated) are retried for a few seconds.
    /// </summary>
    public partial class RoadUpgradeApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<RoadUpgradeCommand> Pending = new InboundQueue<RoadUpgradeCommand>("RoadUpgradeApplySystem", 1024, 8);

        private const float RetrySeconds = 5f;

        // segments still missing are retried a few times a second, not every frame
        private const float RetryInterval = 0.25f;
        private const int MaxWaiting = 4096;
        private const RoadFlags AlignmentFlags = RoadFlags.StartHalfAligned | RoadFlags.EndHalfAligned;

        private PrefabSystem _prefabSystem;
        private readonly List<KeyValuePair<float, EdgeUpgradeData>> _waitingEdges = new List<KeyValuePair<float, EdgeUpgradeData>>();
        private readonly List<KeyValuePair<float, NodeUpgradeData>> _waitingNodes = new List<KeyValuePair<float, NodeUpgradeData>>();
        private float _nextRetry;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            int edges = 0, nodes = 0;

            // new upgrades are tried once straight away; only the ones still missing wait for the retry timer
            var freshEdges = new List<KeyValuePair<float, EdgeUpgradeData>>();
            var freshNodes = new List<KeyValuePair<float, NodeUpgradeData>>();
            while (Pending.TryDequeue(out RoadUpgradeCommand command))
            {
                foreach (EdgeUpgradeData data in command.Edges ?? new EdgeUpgradeData[0])
                {
                    freshEdges.Add(new KeyValuePair<float, EdgeUpgradeData>(now, data));
                }

                foreach (NodeUpgradeData data in command.Nodes ?? new NodeUpgradeData[0])
                {
                    freshNodes.Add(new KeyValuePair<float, NodeUpgradeData>(now, data));
                }
            }

            // oldest first, so two quick changes to one segment end in the latest state: waiting entries are
            // retried (on the timer) before the new ones are tried
            if ((_waitingEdges.Count > 0 || _waitingNodes.Count > 0) && now >= _nextRetry)
            {
                _nextRetry = now + RetryInterval;
                edges += TryEdges(_waitingEdges, now, null);
                nodes += TryNodes(_waitingNodes, now, null);
            }

            edges += TryEdges(freshEdges, now, _waitingEdges);
            nodes += TryNodes(freshNodes, now, _waitingNodes);
            Trim(_waitingEdges, "segment");
            Trim(_waitingNodes, "junction");

            if (edges > 0 || nodes > 0)
            {
                BaseGameLog.Info($"Road upgrades received: {edges} segment(s), {nodes} junction(s) updated");
            }
        }

        private static void Trim<T>(List<KeyValuePair<float, T>> waiting, string what)
        {
            if (waiting.Count <= MaxWaiting)
            {
                return;
            }

            Inbound.Warn("upgrade-waiting", $"Over {MaxWaiting} road upgrades waiting for their {what}; oldest dropped");
            waiting.RemoveRange(0, waiting.Count - MaxWaiting);
        }

        /// <summary>
        ///     Applies the entries whose segment is here. The rest stay in <paramref name="entries" /> (or move to
        ///     <paramref name="waitInto" />) until they are RetrySeconds old.
        /// </summary>
        private int TryEdges(List<KeyValuePair<float, EdgeUpgradeData>> entries, float now, List<KeyValuePair<float, EdgeUpgradeData>> waitInto)
        {
            int applied = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                EdgeUpgradeData data = entries[i].Value;
                bool done = true;
                try
                {
                    if (NetRefs.Resolve(EntityManager, data.Edge, out Entity edge) && edge != Entity.Null)
                    {
                        if (ApplyEdge(edge, data))
                        {
                            applied++;
                        }
                    }
                    else if (now - entries[i].Key > RetrySeconds)
                    {
                        Inbound.Warn("upgrade-edge-missing", $"Road upgrade received for a segment missing here at {NetRefs.V3(data.Edge?.A)}");
                    }
                    else
                    {
                        done = false;
                    }
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(RoadUpgradeApplySystem), e);
                }

                if (!done && waitInto != null)
                {
                    waitInto.Add(entries[i]);
                }
                else if (done && waitInto == null)
                {
                    entries.RemoveAt(i--);
                }
            }

            return applied;
        }

        private int TryNodes(List<KeyValuePair<float, NodeUpgradeData>> entries, float now, List<KeyValuePair<float, NodeUpgradeData>> waitInto)
        {
            int applied = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                NodeUpgradeData data = entries[i].Value;
                bool done = true;
                try
                {
                    if (NetRefs.Resolve(EntityManager, data.Node, out Entity node) && node != Entity.Null)
                    {
                        if (ApplyNode(node, data))
                        {
                            applied++;
                        }
                    }
                    else if (now - entries[i].Key > RetrySeconds)
                    {
                        Inbound.Warn("upgrade-node-missing", $"Road upgrade received for a junction missing here at {NetRefs.V3(data.Node?.A)}");
                    }
                    else
                    {
                        done = false;
                    }
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(RoadUpgradeApplySystem), e);
                }

                if (!done && waitInto != null)
                {
                    waitInto.Add(entries[i]);
                }
                else if (done && waitInto == null)
                {
                    entries.RemoveAt(i--);
                }
            }

            return applied;
        }

        private bool ApplyEdge(Entity entity, EdgeUpgradeData data)
        {
            Entity prefab = PrefabLookup.Find(EntityManager, _prefabSystem, data.Prefab);
            if (prefab == Entity.Null || !EntityManager.HasComponent<NetData>(prefab))
            {
                Inbound.Warn("upgrade-prefab", $"Road upgrade received with an unknown prefab {data.Prefab}");
                return false;
            }

            if (data.Curve == null || data.Curve.Length < 12 || data.StartNode == null)
            {
                BaseGameLog.Warn($"Road upgrade received without a curve for {data.Prefab}");
                return false;
            }

            EntityManager.SetComponentData(entity, new PrefabRef(prefab));
            SetUpgrades(entity, data.HasUpgrades, data.General, data.Left, data.Right);

            // a reversed one-way keeps its junctions but swaps which one it starts at
            Edge edge = EntityManager.GetComponentData<Edge>(entity);
            float3 start = NetRefs.V3(data.StartNode);
            float3 localStart = EntityManager.GetComponentData<Node>(edge.m_Start).m_Position;
            float3 localEnd = EntityManager.GetComponentData<Node>(edge.m_End).m_Position;
            if (math.distancesq(localEnd, start) < math.distancesq(localStart, start))
            {
                EntityManager.SetComponentData(entity, new Edge { m_Start = edge.m_End, m_End = edge.m_Start });
                edge = EntityManager.GetComponentData<Edge>(entity);
            }

            float[] c = data.Curve;
            var bezier = new Bezier4x3(new float3(c[0], c[1], c[2]), new float3(c[3], c[4], c[5]),
                new float3(c[6], c[7], c[8]), new float3(c[9], c[10], c[11]));
            EntityManager.SetComponentData(entity, new Curve { m_Bezier = bezier, m_Length = MathUtils.Length(bezier) });

            if (data.Elevation != null && data.Elevation.Length >= 2)
            {
                var elevation = new Game.Net.Elevation(new float2(data.Elevation[0], data.Elevation[1]));
                if (EntityManager.HasComponent<Game.Net.Elevation>(entity))
                {
                    EntityManager.SetComponentData(entity, elevation);
                }
                else
                {
                    EntityManager.AddComponentData(entity, elevation);
                }
            }
            else if (EntityManager.HasComponent<Game.Net.Elevation>(entity))
            {
                EntityManager.RemoveComponent<Game.Net.Elevation>(entity);
            }

            SetSubReplacements(entity, data.SubReplacements);

            if (data.TramTrack && !EntityManager.HasComponent<TramTrack>(entity))
            {
                EntityManager.AddComponent<TramTrack>(entity);
            }
            else if (!data.TramTrack && EntityManager.HasComponent<TramTrack>(entity))
            {
                EntityManager.RemoveComponent<TramTrack>(entity);
            }

            // only the alignment bits: the rest (lights) is the simulation's
            if (data.RoadAlignment >= 0)
            {
                if (EntityManager.HasComponent<Road>(entity))
                {
                    Road road = EntityManager.GetComponentData<Road>(entity);
                    road.m_Flags = (road.m_Flags & ~AlignmentFlags) | ((RoadFlags)data.RoadAlignment & AlignmentFlags);
                    EntityManager.SetComponentData(entity, road);
                }
                else
                {
                    EntityManager.AddComponentData(entity, new Road { m_Flags = (RoadFlags)data.RoadAlignment & AlignmentFlags });
                }
            }
            else if (EntityManager.HasComponent<Road>(entity))
            {
                EntityManager.RemoveComponent<Road>(entity);
            }

            EntityManager.AddComponent<Updated>(entity);
            MarkUpdated(edge.m_Start);
            MarkUpdated(edge.m_End);
            return true;
        }

        private bool ApplyNode(Entity entity, NodeUpgradeData data)
        {
            Entity prefab = PrefabLookup.Find(EntityManager, _prefabSystem, data.Prefab);
            if (prefab == Entity.Null || !EntityManager.HasComponent<NetData>(prefab))
            {
                Inbound.Warn("upgrade-prefab", $"Junction upgrade received with an unknown prefab {data.Prefab}");
                return false;
            }

            EntityManager.SetComponentData(entity, new PrefabRef(prefab));
            SetUpgrades(entity, data.HasUpgrades, data.General, data.Left, data.Right);
            MarkUpdated(entity);
            return true;
        }

        private void SetUpgrades(Entity entity, bool has, uint general, uint left, uint right)
        {
            if (has)
            {
                var upgraded = new Upgraded
                {
                    m_Flags = new CompositionFlags((CompositionFlags.General)general, (CompositionFlags.Side)left, (CompositionFlags.Side)right)
                };
                if (EntityManager.HasComponent<Upgraded>(entity))
                {
                    EntityManager.SetComponentData(entity, upgraded);
                }
                else
                {
                    EntityManager.AddComponentData(entity, upgraded);
                }
            }
            else if (EntityManager.HasComponent<Upgraded>(entity))
            {
                EntityManager.RemoveComponent<Upgraded>(entity);
            }
        }

        private void SetSubReplacements(Entity entity, SubReplacementData[] subs)
        {
            var resolved = new List<SubReplacement>();
            foreach (SubReplacementData sub in subs ?? new SubReplacementData[0])
            {
                Entity tree = PrefabLookup.Find(EntityManager, _prefabSystem, sub.Prefab);
                if (tree == Entity.Null)
                {
                    BaseGameLog.Warn($"Street tree received with an unknown prefab {sub.Prefab}");
                    continue;
                }

                resolved.Add(new SubReplacement
                {
                    m_Prefab = tree,
                    m_Type = (SubReplacementType)sub.Type,
                    m_Side = (SubReplacementSide)sub.Side,
                    m_AgeMask = (Game.Tools.AgeMask)sub.AgeMask
                });
            }

            if (resolved.Count == 0)
            {
                if (EntityManager.HasBuffer<SubReplacement>(entity))
                {
                    EntityManager.RemoveComponent<SubReplacement>(entity);
                }

                return;
            }

            DynamicBuffer<SubReplacement> buffer = EntityManager.HasBuffer<SubReplacement>(entity)
                ? EntityManager.GetBuffer<SubReplacement>(entity)
                : EntityManager.AddBuffer<SubReplacement>(entity);
            buffer.Clear();
            foreach (SubReplacement sub in resolved)
            {
                buffer.Add(sub);
            }
        }

        // the junction's shape depends on every segment meeting there, as when the tool copies them all
        private void MarkUpdated(Entity node)
        {
            if (node == Entity.Null || !EntityManager.Exists(node))
            {
                return;
            }

            EntityManager.AddComponent<Updated>(node);
            if (!EntityManager.HasBuffer<ConnectedEdge>(node))
            {
                return;
            }

            DynamicBuffer<ConnectedEdge> connected = EntityManager.GetBuffer<ConnectedEdge>(node, true);
            var edges = new List<Entity>();
            for (int i = 0; i < connected.Length; i++)
            {
                edges.Add(connected[i].m_Edge);
            }

            foreach (Entity edge in edges)
            {
                EntityManager.AddComponent<Updated>(edge);
            }
        }
    }
}
