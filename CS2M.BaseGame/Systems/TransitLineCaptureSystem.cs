using System.Collections.Generic;
using System.Text;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using RouteColor = Game.Routes.Color;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Converts transport lines to and from LineRef (prefab + waypoint positions).
    /// </summary>
    internal static class Lines
    {
        // waypoint positions are copied exactly, so this only absorbs float noise
        private const float MatchDistanceSq = 0.01f;

        public static uint Pack(Color32 c) => c.r | (uint)c.g << 8 | (uint)c.b << 16 | (uint)c.a << 24;

        public static Color32 Unpack(uint v) => new Color32((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24));

        public static float3 WaypointPosition(EntityManager em, Entity waypoint) =>
            em.HasComponent<Position>(waypoint) ? em.GetComponentData<Position>(waypoint).m_Position : float3.zero;

        public static LineRef Describe(EntityManager em, PrefabSystem prefabs, Entity route)
        {
            DynamicBuffer<RouteWaypoint> waypoints = em.GetBuffer<RouteWaypoint>(route, true);
            var flat = new float[waypoints.Length * 3];
            for (int i = 0; i < waypoints.Length; i++)
            {
                float3 p = WaypointPosition(em, waypoints[i].m_Waypoint);
                flat[i * 3] = p.x;
                flat[i * 3 + 1] = p.y;
                flat[i * 3 + 2] = p.z;
            }

            return new LineRef { Prefab = PrefabLookup.Name(prefabs, em.GetComponentData<PrefabRef>(route).m_Prefab), Waypoints = flat };
        }

        public static string Key(LineRef line)
        {
            var key = new StringBuilder(line.Prefab ?? "");
            foreach (float f in line.Waypoints ?? new float[0])
            {
                key.Append(',').Append(f.ToString("F2"));
            }

            return key.ToString();
        }

        /// <summary>
        ///     The local line with the same prefab and waypoints, or Entity.Null.
        /// </summary>
        public static Entity Find(EntityManager em, PrefabSystem prefabs, LineRef line)
        {
            if (line?.Waypoints == null)
            {
                return Entity.Null;
            }

            Entity prefab = PrefabLookup.Find(em, prefabs, line.Prefab);
            if (prefab == Entity.Null)
            {
                return Entity.Null;
            }

            int count = line.Waypoints.Length / 3;
            using (EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<RouteWaypoint>(), ComponentType.ReadOnly<PrefabRef>() },
                       None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
                   }))
            using (NativeArray<Entity> routes = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity route in routes)
                {
                    if (em.GetComponentData<PrefabRef>(route).m_Prefab != prefab)
                    {
                        continue;
                    }

                    DynamicBuffer<RouteWaypoint> waypoints = em.GetBuffer<RouteWaypoint>(route, true);
                    if (waypoints.Length != count)
                    {
                        continue;
                    }

                    bool same = true;
                    for (int i = 0; i < count && same; i++)
                    {
                        float3 a = WaypointPosition(em, waypoints[i].m_Waypoint);
                        float3 b = new float3(line.Waypoints[i * 3], line.Waypoints[i * 3 + 1], line.Waypoints[i * 3 + 2]);
                        same = math.distancesq(a, b) < MatchDistanceSq;
                    }

                    if (same)
                    {
                        return route;
                    }
                }
            }

            return Entity.Null;
        }
    }

    /// <summary>
    ///     Runs in the ApplyTool phase before ApplyRoutesSystem. While the route tool is active, the temp line it
    ///     is about to apply (a new line, or an existing one with waypoints added, moved or removed) is sent with
    ///     its full waypoint list. Removals are sent by TransitLineDeleteCaptureSystem, whatever removed the line.
    /// </summary>
    public partial class TransitLineCaptureSystem : GameSystemBase
    {
        private const float LocallyCreatedTimeout = 10f;

        // lines this player just made: TransitLineSettingsSystem sends their colour/number once they exist
        private static readonly Dictionary<string, float> LocallyCreated = new Dictionary<string, float>();

        private EntityQuery _tempRoutes;
        private ToolSystem _toolSystem;
        private PrefabSystem _prefabSystem;
        private string _lastKey;
        private float _lastTime;

        public static bool TakeLocallyCreated(string key)
        {
            if (!LocallyCreated.TryGetValue(key, out float time))
            {
                return false;
            }

            LocallyCreated.Remove(key);
            return UnityEngine.Time.realtimeSinceStartup - time < LocallyCreatedTimeout;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _tempRoutes = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Route>(),
                ComponentType.ReadOnly<RouteWaypoint>(),
                ComponentType.ReadOnly<PrefabRef>());
            RequireForUpdate(_tempRoutes);
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null ||
                !(_toolSystem.activeTool is RouteToolSystem))
            {
                return;
            }

            using (NativeArray<Entity> entities = _tempRoutes.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    Capture(entity);
                }
            }
        }

        private void Capture(Entity entity)
        {
            Temp temp = EntityManager.GetComponentData<Temp>(entity);
            if ((temp.m_Flags & (TempFlags.Delete | TempFlags.Select)) != 0 ||
                (EntityManager.GetComponentData<Route>(entity).m_Flags & RouteFlags.Complete) == 0)
            {
                return;
            }

            bool isNew = temp.m_Original == Entity.Null;
            if (!isNew && !EntityManager.HasBuffer<RouteWaypoint>(temp.m_Original))
            {
                return;
            }

            string prefab = PrefabLookup.Name(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
            if (prefab == null)
            {
                return;
            }

            DynamicBuffer<RouteWaypoint> buffer = EntityManager.GetBuffer<RouteWaypoint>(entity, true);
            var waypoints = new LineWaypointData[buffer.Length];
            var flat = new float[buffer.Length * 3];
            for (int i = 0; i < buffer.Length; i++)
            {
                Entity waypoint = buffer[i].m_Waypoint;
                float3 p = Lines.WaypointPosition(EntityManager, waypoint);
                flat[i * 3] = p.x;
                flat[i * 3 + 1] = p.y;
                flat[i * 3 + 2] = p.z;
                waypoints[i] = new LineWaypointData { Position = NetRefs.F(p), Connection = Connection(waypoint) };
            }

            var line = new LineRef { Prefab = prefab, Waypoints = flat };
            LineRef original = isNew ? null : Lines.Describe(EntityManager, _prefabSystem, temp.m_Original);
            string key = Lines.Key(line) + "|" + (original != null ? Lines.Key(original) : "new");
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (original != null && Lines.Key(original) == Lines.Key(line) && SameConnections(temp.m_Original, waypoints))
            {
                // nothing changed (e.g. a waypoint dragged back to where it was)
                return;
            }

            if (key == _lastKey && now - _lastTime < 0.5f)
            {
                return;
            }

            _lastKey = key;
            _lastTime = now;
            uint color = EntityManager.HasComponent<RouteColor>(entity)
                ? Lines.Pack(EntityManager.GetComponentData<RouteColor>(entity).m_Color)
                : 0xFFFFFFFF;
            NetRef owner = isNew && EntityManager.HasComponent<Owner>(entity)
                ? NetRefs.Describe(EntityManager, EntityManager.GetComponentData<Owner>(entity).m_Owner, _prefabSystem)
                : null;
            Command.SendToAll(new TransitLineCommand { Prefab = prefab, Original = original, Waypoints = waypoints, Color = color, Owner = owner });
            if (isNew)
            {
                LocallyCreated[Lines.Key(line)] = now;
            }

            int stops = 0;
            foreach (LineWaypointData w in waypoints)
            {
                stops += w.Connection != null && w.Connection.Kind != 0 ? 1 : 0;
            }

            BaseGameLog.Info($"Transit line sent: {(isNew ? "new" : "changed")} {prefab}, {waypoints.Length} waypoint(s), {stops} stop(s)");
        }

        private NetRef Connection(Entity waypoint)
        {
            if (!EntityManager.HasComponent<Connected>(waypoint))
            {
                return null;
            }

            Entity connected = EntityManager.GetComponentData<Connected>(waypoint).m_Connected;
            // a stop highlighted by the tool can be a temp copy; describe the real one
            if (connected != Entity.Null && EntityManager.HasComponent<Temp>(connected))
            {
                connected = EntityManager.GetComponentData<Temp>(connected).m_Original;
            }

            return connected == Entity.Null ? null : NetRefs.Describe(EntityManager, connected, _prefabSystem);
        }

        private bool SameConnections(Entity route, LineWaypointData[] waypoints)
        {
            DynamicBuffer<RouteWaypoint> buffer = EntityManager.GetBuffer<RouteWaypoint>(route, true);
            for (int i = 0; i < buffer.Length && i < waypoints.Length; i++)
            {
                NetRef a = Connection(buffer[i].m_Waypoint);
                NetRef b = waypoints[i].Connection;
                if ((a?.Kind ?? 0) != (b?.Kind ?? 0) || (a != null && a.Kind != 0 &&
                        (a.Prefab != b.Prefab || math.distancesq(NetRefs.V3(a.A), NetRefs.V3(b.A)) > 0.01f)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    ///     Runs at the end of the modification phases, while removed lines still exist with a Deleted tag.
    ///     Catches every way a player removes a line (route tool, line list, info panel) in one place.
    /// </summary>
    public partial class TransitLineDeleteCaptureSystem : GameSystemBase
    {
        /// <summary>
        ///     Lines removed by TransitLineApplySystem, which must not be sent back.
        /// </summary>
        public static readonly HashSet<Entity> RemovedByNetwork = new HashSet<Entity>();

        private EntityQuery _deleted;
        private PrefabSystem _prefabSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _deleted = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<RouteWaypoint>(),
                    ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Deleted>()
                },
                None = new[] { ComponentType.ReadOnly<Temp>() }
            });
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null || _deleted.IsEmptyIgnoreFilter)
            {
                RemovedByNetwork.Clear();
                return;
            }

            using (NativeArray<Entity> entities = _deleted.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (RemovedByNetwork.Contains(entity) || OwnerRemoved(entity))
                    {
                        continue;
                    }

                    LineRef line = Lines.Describe(EntityManager, _prefabSystem, entity);
                    if (line.Prefab == null)
                    {
                        continue;
                    }

                    Command.SendToAll(new TransitLineDeleteCommand { Line = line });
                    BaseGameLog.Info($"Transit line removal sent: {line.Prefab} with {line.Waypoints.Length / 3} waypoint(s)");
                }
            }

            RemovedByNetwork.Clear();
        }

        // a line that belongs to a building is removed with it on every game
        private bool OwnerRemoved(Entity entity)
        {
            if (!EntityManager.HasComponent<Owner>(entity))
            {
                return false;
            }

            Entity owner = EntityManager.GetComponentData<Owner>(entity).m_Owner;
            return owner == Entity.Null || !EntityManager.Exists(owner) || EntityManager.HasComponent<Deleted>(owner);
        }
    }
}
