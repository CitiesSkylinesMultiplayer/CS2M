using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Policies;
using Game.Prefabs;
using Game.Routes;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using RouteColor = Game.Routes.Color;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Applies other players' line changes. Lines are written directly as finished entities (the same
    ///     archetypes and components ApplyRoutesSystem leaves behind), because the route tool's temp pipeline
    ///     only works for the local tool: GenerateRoutesSystem always makes Temp entities and pairs every temp
    ///     waypoint in the world with every definition, so it would mix with the local player's preview.
    ///     The game's own systems then connect the waypoints to lanes and stops, number the line and find paths.
    /// </summary>
    public partial class TransitLineApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<CommandBase> Pending = new InboundQueue<CommandBase>("TransitLineApplySystem", 1024, 32);

        // a stop placed just before the line may still be under construction here
        private const float ConnectionWait = 5f;
        private const float SettingsWait = 10f;
        private const float MatchDistanceSq = 0.01f;
        private const float ResolveInterval = 0.25f;

        private const int MaxOrdered = 1024;
        private const int MaxSettings = 256;

        private class WaitingSettings
        {
            public TransitLineSettingsCommand Command;
            public float Since;
            public string Key;
        }

        // a line whose stops aren't all here yet: what's resolved so far, and since when it waits
        private class WaitingLine
        {
            public float Since;
            public float LastTry;
            public Entity[] Connections;
        }

        private readonly List<CommandBase> _ordered = new List<CommandBase>();
        private readonly Dictionary<TransitLineCommand, WaitingLine> _waitingLines = new Dictionary<TransitLineCommand, WaitingLine>();
        private readonly List<WaitingSettings> _settings = new List<WaitingSettings>();
        // each queued command's line keys, worked out once
        private readonly Dictionary<CommandBase, string[]> _keys = new Dictionary<CommandBase, string[]>();
        private float _lastSettingsTry;

        private PrefabSystem _prefabSystem;
        private NameSystem _nameSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();
        }

        protected override void OnUpdate()
        {
            // received while joining: wait until the city has loaded
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;

            // settings waiting for their line go first, a few times a second. A line built here still carries
            // Created until the game has assigned its own number (next frame), which would otherwise overwrite
            // the one received
            if (_settings.Count > 0 && now - _lastSettingsTry >= ResolveInterval)
            {
                _lastSettingsTry = now;
                for (int i = 0; i < _settings.Count; i++)
                {
                    WaitingSettings waiting = _settings[i];
                    try
                    {
                        Entity route = Lines.Find(EntityManager, _prefabSystem, waiting.Command.Line);
                        if (route != Entity.Null && !EntityManager.HasComponent<Created>(route))
                        {
                            ApplySettings(route, waiting.Command);
                        }
                        else if (now - waiting.Since > SettingsWait)
                        {
                            Inbound.Warn("line-settings-missing", $"Transit line settings received for a line missing here ({waiting.Command.Line?.Prefab})");
                        }
                        else
                        {
                            continue;
                        }
                    }
                    catch (System.Exception e)
                    {
                        Inbound.Failed(nameof(TransitLineApplySystem), e);
                    }

                    _settings.RemoveAt(i--);
                }
            }

            while (Pending.TryDequeue(out CommandBase command))
            {
                _ordered.Add(command);
            }

            if (_ordered.Count > MaxOrdered)
            {
                Inbound.Warn("line-ordered", $"Over {MaxOrdered} transit line changes waiting; oldest dropped");
                foreach (CommandBase dropped in _ordered.GetRange(0, _ordered.Count - MaxOrdered))
                {
                    if (dropped is TransitLineCommand line)
                    {
                        _waitingLines.Remove(line);
                    }

                    _keys.Remove(dropped);
                }

                _ordered.RemoveRange(0, _ordered.Count - MaxOrdered);
            }

            // In order per line, so a change never overtakes the line's creation. A line waiting for its stops
            // holds back only the later commands for the same line (by its old or new waypoints), not the rest.
            HashSet<string> blocked = null;
            for (int i = 0; i < _ordered.Count; i++)
            {
                CommandBase command = _ordered[i];
                bool done = true;
                try
                {
                    if (Touches(command, blocked))
                    {
                        Block(command, ref blocked);
                        done = false;
                    }
                    else if (command is TransitLineSettingsCommand settings)
                    {
                        // applied right away when the line is already settled, so a reshape queued right after it
                        // can't move the line away from the waypoints this command names
                        Entity route = Lines.Find(EntityManager, _prefabSystem, settings.Line);
                        if (route != Entity.Null && !EntityManager.HasComponent<Created>(route))
                        {
                            ApplySettings(route, settings);
                        }
                        else
                        {
                            WaitForLine(settings, now);
                        }
                    }
                    else if (command is TransitLineDeleteCommand delete)
                    {
                        Delete(delete);
                    }
                    else if (command is TransitLineCommand line)
                    {
                        if (!Ready(line, now, out Entity[] connections, out int missing))
                        {
                            Block(command, ref blocked);
                            done = false;
                        }
                        else
                        {
                            if (missing > 0)
                            {
                                Inbound.Warn("line-stops-missing", $"Transit line received with {missing} stop(s) missing here; those waypoints are built without a stop");
                            }

                            Build(line, connections);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(TransitLineApplySystem), e);
                    done = true;
                }

                if (done)
                {
                    if (command is TransitLineCommand finished)
                    {
                        _waitingLines.Remove(finished);
                    }

                    _keys.Remove(command);
                    _ordered.RemoveAt(i--);
                }
            }
        }

        /// <summary>
        ///     Whether a line's stops are all here, or it has waited long enough. Only the stops still missing are
        ///     looked up again, a few times a second.
        /// </summary>
        private bool Ready(TransitLineCommand line, float now, out Entity[] connections, out int missing)
        {
            if (!_waitingLines.TryGetValue(line, out WaitingLine waiting))
            {
                waiting = new WaitingLine
                {
                    Since = now, LastTry = float.NegativeInfinity, Connections = new Entity[line.Waypoints?.Length ?? 0]
                };
                _waitingLines[line] = waiting;
            }

            connections = waiting.Connections;
            missing = 0;
            bool retry = now - waiting.LastTry >= ResolveInterval;
            if (retry)
            {
                waiting.LastTry = now;
            }

            for (int i = 0; i < connections.Length; i++)
            {
                NetRef connection = line.Waypoints[i].Connection;
                if (connection == null || connection.Kind == 0 || connections[i] != Entity.Null)
                {
                    continue;
                }

                if (retry && NetRefs.Resolve(EntityManager, connection, out Entity found, _prefabSystem) && found != Entity.Null)
                {
                    connections[i] = found;
                    continue;
                }

                missing++;
            }

            return missing == 0 || now - waiting.Since >= ConnectionWait;
        }

        private void WaitForLine(TransitLineSettingsCommand settings, float now)
        {
            // a newer settings command for the same line replaces the waiting one
            string key = Lines.Key(settings.Line);
            _settings.RemoveAll(w => w.Key == key);
            _settings.Add(new WaitingSettings { Command = settings, Since = now, Key = key });
            if (_settings.Count > MaxSettings)
            {
                Inbound.Warn("line-settings-waiting", $"Over {MaxSettings} transit line settings waiting for their line; oldest dropped");
                _settings.RemoveRange(0, _settings.Count - MaxSettings);
            }
        }

        // the lines a command refers to (by waypoints) and, for a line change, the line it becomes
        private string[] Keys(CommandBase command)
        {
            if (_keys.TryGetValue(command, out string[] keys))
            {
                return keys;
            }

            switch (command)
            {
                case TransitLineSettingsCommand settings:
                    keys = new[] { Lines.Key(settings.Line) };
                    break;
                case TransitLineDeleteCommand delete:
                    keys = new[] { Lines.Key(delete.Line) };
                    break;
                case TransitLineCommand line when line.Original != null:
                    keys = new[] { Lines.Key(line.Original), Lines.Key(Result(line)) };
                    break;
                case TransitLineCommand line:
                    keys = new[] { Lines.Key(Result(line)) };
                    break;
                default:
                    keys = new string[0];
                    break;
            }

            _keys[command] = keys;
            return keys;
        }

        private static LineRef Result(TransitLineCommand line)
        {
            int count = line.Waypoints?.Length ?? 0;
            var flat = new float[count * 3];
            for (int i = 0; i < count; i++)
            {
                float3 p = NetRefs.V3(line.Waypoints[i].Position);
                flat[i * 3] = p.x;
                flat[i * 3 + 1] = p.y;
                flat[i * 3 + 2] = p.z;
            }

            return new LineRef { Prefab = line.Prefab, Waypoints = flat };
        }

        private bool Touches(CommandBase command, HashSet<string> blocked)
        {
            if (blocked == null)
            {
                return false;
            }

            foreach (string key in Keys(command))
            {
                if (blocked.Contains(key))
                {
                    return true;
                }
            }

            return false;
        }

        private void Block(CommandBase command, ref HashSet<string> blocked)
        {
            blocked ??= new HashSet<string>();
            foreach (string key in Keys(command))
            {
                blocked.Add(key);
            }
        }

        private void Delete(TransitLineDeleteCommand command)
        {
            Entity route = Lines.Find(EntityManager, _prefabSystem, command.Line);
            if (route == Entity.Null)
            {
                BaseGameLog.Warn($"Transit line removal received for a line missing here ({command.Line?.Prefab})");
                return;
            }

            // the same as ApplyRoutesSystem does for a line deleted with the route tool
            var buffer = new EntityCommandBuffer(Allocator.Temp);
            foreach (RouteWaypoint waypoint in EntityManager.GetBuffer<RouteWaypoint>(route, true))
            {
                buffer.AddComponent<Deleted>(waypoint.m_Waypoint);
            }

            if (EntityManager.HasBuffer<RouteSegment>(route))
            {
                foreach (RouteSegment segment in EntityManager.GetBuffer<RouteSegment>(route, true))
                {
                    buffer.AddComponent<Deleted>(segment.m_Segment);
                }
            }

            buffer.AddComponent<Deleted>(route);
            buffer.Playback(EntityManager);
            buffer.Dispose();
            TransitLineDeleteCaptureSystem.RemovedByNetwork.Add(route);
            BaseGameLog.Info($"Transit line removal received: {command.Line.Prefab}");
        }

        private void Build(TransitLineCommand command, Entity[] connections)
        {
            Entity prefab = PrefabLookup.Find(EntityManager, _prefabSystem, command.Prefab);
            if (prefab == Entity.Null || !EntityManager.HasComponent<RouteData>(prefab))
            {
                BaseGameLog.Warn($"Transit line received with an unknown prefab {command.Prefab}");
                return;
            }

            if (command.Waypoints == null || command.Waypoints.Length < 2)
            {
                return;
            }

            RouteData routeData = EntityManager.GetComponentData<RouteData>(prefab);
            if (!routeData.m_RouteArchetype.Valid || !routeData.m_WaypointArchetype.Valid)
            {
                return;
            }

            if (command.Original == null)
            {
                Create(command, prefab, routeData, connections);
                BaseGameLog.Info($"Transit line received: new {command.Prefab} with {command.Waypoints.Length} waypoint(s)");
                return;
            }

            Entity route = Lines.Find(EntityManager, _prefabSystem, command.Original);
            if (route == Entity.Null)
            {
                BaseGameLog.Warn($"Transit line change received for a line missing here ({command.Prefab})");
                return;
            }

            Modify(route, command, prefab, routeData, connections);
            BaseGameLog.Info($"Transit line received: {command.Prefab} changed to {command.Waypoints.Length} waypoint(s)");
        }

        private void Create(TransitLineCommand command, Entity prefab, RouteData routeData, Entity[] connections)
        {
            var buffer = new EntityCommandBuffer(Allocator.Temp);
            Entity route = buffer.CreateEntity(routeData.m_RouteArchetype);
            buffer.SetComponent(route, new Route { m_Flags = RouteFlags.Complete });
            buffer.SetComponent(route, new PrefabRef(prefab));
            buffer.SetComponent(route, new RouteColor(Lines.Unpack(command.Color)));
            if (EntityManager.HasComponent<TransportLineData>(prefab))
            {
                buffer.SetComponent(route, new TransportLine(EntityManager.GetComponentData<TransportLineData>(prefab)));
            }

            if (command.Owner != null && command.Owner.Kind != 0)
            {
                // GenerateRoutesSystem adds Owner the same way when the tool was opened from a building
                if (NetRefs.Resolve(EntityManager, command.Owner, out Entity owner, _prefabSystem) && owner != Entity.Null)
                {
                    buffer.AddComponent(route, new Owner(owner));
                }
                else
                {
                    BaseGameLog.Warn($"Transit line received for a building missing here ({command.Owner.Prefab}); built without it");
                }
            }

            int count = command.Waypoints.Length;
            var waypoints = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                waypoints[i] = NewWaypoint(buffer, routeData, prefab, route, i, NetRefs.V3(command.Waypoints[i].Position), connections[i]);
            }

            DynamicBuffer<RouteWaypoint> routeWaypoints = buffer.SetBuffer<RouteWaypoint>(route);
            foreach (Entity waypoint in waypoints)
            {
                routeWaypoints.Add(new RouteWaypoint(waypoint));
            }

            // a finished line is a loop: segment i runs from waypoint i to the next, the last back to the first
            DynamicBuffer<RouteSegment> routeSegments = buffer.SetBuffer<RouteSegment>(route);
            for (int i = 0; i < count; i++)
            {
                routeSegments.Add(new RouteSegment(NewSegment(buffer, routeData, prefab, route, i)));
            }

            buffer.Playback(EntityManager);
            buffer.Dispose();
        }

        /// <summary>
        ///     Rewrites a line's waypoints and segments, keeping the ones that didn't change (so vehicles and
        ///     paths on them survive), the way ApplyRoutesSystem updates the original line.
        /// </summary>
        private void Modify(Entity route, TransitLineCommand command, Entity prefab, RouteData routeData, Entity[] connections)
        {
            var oldWaypoints = new List<Entity>();
            foreach (RouteWaypoint waypoint in EntityManager.GetBuffer<RouteWaypoint>(route, true))
            {
                oldWaypoints.Add(waypoint.m_Waypoint);
            }

            var oldSegments = new List<Entity>();
            if (EntityManager.HasBuffer<RouteSegment>(route))
            {
                foreach (RouteSegment segment in EntityManager.GetBuffer<RouteSegment>(route, true))
                {
                    oldSegments.Add(segment.m_Segment);
                }
            }

            var buffer = new EntityCommandBuffer(Allocator.Temp);
            int count = command.Waypoints.Length;
            var waypoints = new Entity[count];
            var kept = new HashSet<Entity>();
            for (int i = 0; i < count; i++)
            {
                float3 position = NetRefs.V3(command.Waypoints[i].Position);
                Entity reuse = Entity.Null;
                foreach (Entity old in oldWaypoints)
                {
                    if (!kept.Contains(old) &&
                        math.distancesq(Lines.WaypointPosition(EntityManager, old), position) < MatchDistanceSq &&
                        ConnectionOf(old) == connections[i])
                    {
                        reuse = old;
                        break;
                    }
                }

                if (reuse != Entity.Null)
                {
                    kept.Add(reuse);
                    buffer.SetComponent(reuse, new Waypoint(i));
                    buffer.AddComponent<Updated>(reuse);
                    waypoints[i] = reuse;
                }
                else
                {
                    waypoints[i] = NewWaypoint(buffer, routeData, prefab, route, i, position, connections[i]);
                }
            }

            foreach (Entity old in oldWaypoints)
            {
                if (!kept.Contains(old))
                {
                    buffer.AddComponent<Deleted>(old);
                }
            }

            // a segment survives when both its ends survived as neighbours in the same direction
            var segments = new Entity[count];
            var keptSegments = new HashSet<Entity>();
            for (int i = 0; i < count; i++)
            {
                Entity start = waypoints[i], end = waypoints[(i + 1) % count];
                foreach (Entity old in oldSegments)
                {
                    if (keptSegments.Contains(old) || !EntityManager.HasComponent<Segment>(old) || oldWaypoints.Count == 0)
                    {
                        continue;
                    }

                    int index = EntityManager.GetComponentData<Segment>(old).m_Index;
                    if (index < 0 || index >= oldWaypoints.Count ||
                        oldWaypoints[index] != start || oldWaypoints[(index + 1) % oldWaypoints.Count] != end)
                    {
                        continue;
                    }

                    keptSegments.Add(old);
                    buffer.SetComponent(old, new Segment(i));
                    buffer.AddComponent<Updated>(old);
                    segments[i] = old;
                    break;
                }

                if (segments[i] == Entity.Null)
                {
                    segments[i] = NewSegment(buffer, routeData, prefab, route, i);
                }
            }

            foreach (Entity old in oldSegments)
            {
                if (!keptSegments.Contains(old))
                {
                    buffer.AddComponent<Deleted>(old);
                }
            }

            DynamicBuffer<RouteWaypoint> routeWaypoints = buffer.SetBuffer<RouteWaypoint>(route);
            foreach (Entity waypoint in waypoints)
            {
                routeWaypoints.Add(new RouteWaypoint(waypoint));
            }

            DynamicBuffer<RouteSegment> routeSegments = buffer.SetBuffer<RouteSegment>(route);
            foreach (Entity segment in segments)
            {
                routeSegments.Add(new RouteSegment(segment));
            }

            buffer.AddComponent<Updated>(route);
            buffer.Playback(EntityManager);
            buffer.Dispose();
        }

        private Entity ConnectionOf(Entity waypoint) =>
            EntityManager.HasComponent<Connected>(waypoint) ? EntityManager.GetComponentData<Connected>(waypoint).m_Connected : Entity.Null;

        // same components as GenerateWaypointsSystem gives a waypoint; the archetype already has Created + Updated
        private static Entity NewWaypoint(EntityCommandBuffer buffer, RouteData routeData, Entity prefab, Entity route, int index, float3 position, Entity connection)
        {
            Entity waypoint;
            if (connection != Entity.Null && routeData.m_ConnectedArchetype.Valid)
            {
                waypoint = buffer.CreateEntity(routeData.m_ConnectedArchetype);
                buffer.SetComponent(waypoint, new Connected(connection));
            }
            else
            {
                waypoint = buffer.CreateEntity(routeData.m_WaypointArchetype);
            }

            buffer.SetComponent(waypoint, new Waypoint(index));
            buffer.SetComponent(waypoint, new Position(position));
            buffer.SetComponent(waypoint, new PrefabRef(prefab));
            buffer.SetComponent(waypoint, new Owner(route));
            return waypoint;
        }

        private static Entity NewSegment(EntityCommandBuffer buffer, RouteData routeData, Entity prefab, Entity route, int index)
        {
            Entity segment = buffer.CreateEntity(routeData.m_SegmentArchetype);
            buffer.SetComponent(segment, new Segment(index));
            buffer.SetComponent(segment, new PrefabRef(prefab));
            buffer.SetComponent(segment, new Owner(route));
            return segment;
        }

        /// <summary>
        ///     Applies colour, name and number directly and policies through Modify events, the same way the
        ///     line panels do (ModifiedSystem then recalculates the line's options and modifiers).
        /// </summary>
        private void ApplySettings(Entity route, TransitLineSettingsCommand command)
        {
            TransitLineSettingsSystem.Suppress(route);
            var buffer = new EntityCommandBuffer(Allocator.Temp);
            if (EntityManager.HasComponent<RouteColor>(route) &&
                Lines.Pack(EntityManager.GetComponentData<RouteColor>(route).m_Color) != command.Color)
            {
                var color = new RouteColor(Lines.Unpack(command.Color));
                buffer.SetComponent(route, color);
                if (EntityManager.HasBuffer<RouteVehicle>(route))
                {
                    foreach (RouteVehicle vehicle in EntityManager.GetBuffer<RouteVehicle>(route, true))
                    {
                        buffer.AddComponent(vehicle.m_Vehicle, color);
                    }
                }

                Entity colorEvent = buffer.CreateEntity();
                buffer.AddComponent<Event>(colorEvent);
                buffer.AddComponent(colorEvent, new ColorUpdated(route));
            }

            if (EntityManager.HasComponent<RouteNumber>(route) &&
                EntityManager.GetComponentData<RouteNumber>(route).m_Number != command.Number)
            {
                buffer.SetComponent(route, new RouteNumber { m_Number = command.Number });
            }

            var local = new Dictionary<Entity, Policy>();
            if (EntityManager.HasBuffer<Policy>(route))
            {
                foreach (Policy policy in EntityManager.GetBuffer<Policy>(route, true))
                {
                    local[policy.m_Policy] = policy;
                }
            }

            var received = new HashSet<Entity>();
            foreach (LinePolicyData data in command.Policies ?? new LinePolicyData[0])
            {
                Entity policy = PrefabLookup.Find(EntityManager, _prefabSystem, data.Policy);
                if (policy == Entity.Null)
                {
                    continue;
                }

                received.Add(policy);
                if (local.TryGetValue(policy, out Policy current) &&
                    ((current.m_Flags & PolicyFlags.Active) != 0) == data.Active && current.m_Adjustment == data.Adjustment)
                {
                    continue;
                }

                ModifyPolicy(buffer, route, policy, data.Active, Inbound.PolicyAdjustment(EntityManager, policy, data.Adjustment));
            }

            foreach (KeyValuePair<Entity, Policy> pair in local)
            {
                if (!received.Contains(pair.Key) && (pair.Value.m_Flags & PolicyFlags.Active) != 0)
                {
                    ModifyPolicy(buffer, route, pair.Key, false, pair.Value.m_Adjustment);
                }
            }

            buffer.Playback(EntityManager);
            buffer.Dispose();

            _nameSystem.TryGetCustomName(route, out string name);
            if ((name ?? "") != (command.Name ?? ""))
            {
                _nameSystem.SetCustomName(route, command.Name ?? "");
            }

            BaseGameLog.Info($"Transit line settings received: {command.Line.Prefab} #{command.Number}");
        }

        private static void ModifyPolicy(EntityCommandBuffer buffer, Entity route, Entity policy, bool active, float adjustment)
        {
            Entity modify = buffer.CreateEntity();
            buffer.AddComponent<Event>(modify);
            buffer.AddComponent(modify, new Modify(route, policy, active, adjustment));
        }
    }
}
