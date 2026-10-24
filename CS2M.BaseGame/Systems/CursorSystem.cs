using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Input;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Shares the local cursor (where the mouse hits the terrain) and keeps the other players' cursors.
    ///     Runs in the Raycast phase: reads last frame's terrain hit, then queues this frame's ray.
    /// </summary>
    public partial class CursorSystem : GameSystemBase
    {
        public class RemoteCursor
        {
            // what's shown: BaseName, plus the player id when another player uses the same name
            public string Name;
            // the cleaned name the player sent
            public string BaseName;
            // Position is what's drawn; it glides towards Target, the last position received
            public float3 Position;
            public float3 Target;
            public bool Placed;
            public string Activity;
            public bool OnMap;
            public float LastSeen;
            public Color Color;
        }

        /// <summary>
        ///     Other players' cursors by sender id. Only touched on the main thread.
        /// </summary>
        public static readonly Dictionary<int, RemoteCursor> Remote = new Dictionary<int, RemoteCursor>();

        /// <summary>
        ///     A cursor that hasn't been heard from in this many seconds is dropped (player left).
        /// </summary>
        public const float Timeout = 6f;

        /// <summary>
        ///     Goes up whenever a cursor joins or leaves or a name or activity changes, so the UI only rebuilds
        ///     the player list when something it shows changed.
        /// </summary>
        public static int Version { get; private set; }

        private const int MaxNameLength = 32;
        private const int MaxRemoteCursors = 64;

        // the only activities a cursor can show (see Activity()); anything else is shown as nothing
        private static readonly HashSet<string> Activities = new HashSet<string>
        {
            "", "zoning", "roads", "bulldozing", "placing", "areas", "transit lines", "terrain", "upgrading", "in menu", "away"
        };

        // a name a remote player may not show up as: the mod's own chat messages use it
        private const string ReservedName = "CS2M";

        private const float SendInterval = 0.05f;
        private const float Heartbeat = 2f;

        private static readonly Color[] Palette =
        {
            new Color(0.25f, 0.75f, 1f), new Color(1f, 0.55f, 0.2f), new Color(0.6f, 1f, 0.3f),
            new Color(1f, 0.35f, 0.65f), new Color(0.75f, 0.5f, 1f), new Color(1f, 0.9f, 0.3f),
            new Color(0.3f, 1f, 0.85f), new Color(1f, 0.4f, 0.35f)
        };

        private RaycastSystem _raycastSystem;
        private CameraUpdateSystem _cameraUpdateSystem;
        private ToolSystem _toolSystem;

        private float3 _lastSentPosition;
        private string _lastSentActivity = "";
        private bool _lastSentOnMap;
        private float _lastSentTime;

        protected override void OnCreate()
        {
            base.OnCreate();
            _raycastSystem = World.GetOrCreateSystemManaged<RaycastSystem>();
            _cameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                if (Remote.Count > 0)
                {
                    Remote.Clear();
                    Version++;
                }

                return;
            }

            DropStale();
            Smooth();

            NativeArray<RaycastResult> results = _raycastSystem.GetResult(this);
            // no hit leaves the position at zero
            bool onMap = results.Length != 0 && math.lengthsq(results[0].m_Hit.m_HitPosition) > 0f &&
                         InputManager.instance.controlOverWorld;
            float3 hit = onMap ? results[0].m_Hit.m_HitPosition : _lastSentPosition;
            Send(hit, onMap);

            if (_cameraUpdateSystem.TryGetViewer(out Viewer viewer))
            {
                RaycastInput input = default;
                input.m_Line = ToolRaycastSystem.CalculateRaycastLine(viewer.camera);
                input.m_TypeMask = TypeMask.Terrain | TypeMask.Water;
                _raycastSystem.AddInput(this, input);
            }
        }

        private void Send(float3 position, bool onMap)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            string activity = Activity();
            bool changed = onMap != _lastSentOnMap || activity != _lastSentActivity ||
                           (onMap && math.distancesq(position, _lastSentPosition) > 1f);
            if (now - _lastSentTime < SendInterval || (!changed && now - _lastSentTime < Heartbeat))
            {
                return;
            }

            Command.SendToAll?.Invoke(new CursorCommand
            {
                Username = Command.GetLocalUsername?.Invoke() ?? "",
                X = position.x,
                Y = position.y,
                Z = position.z,
                Activity = activity,
                OnMap = onMap
            });
            _lastSentPosition = position;
            _lastSentActivity = activity;
            _lastSentOnMap = onMap;
            _lastSentTime = now;
        }

        private string Activity()
        {
            if (Presence.Away)
            {
                return "away";
            }

            if (Presence.InMenu)
            {
                return "in menu";
            }

            string tool = _toolSystem.activeTool?.toolID ?? "";
            switch (tool)
            {
                case "Zone Tool": return "zoning";
                case "Net Tool": return "roads";
                case "Bulldoze Tool": return "bulldozing";
                case "Object Tool": return "placing";
                case "Area Tool": return "areas";
                case "Route Tool": return "transit lines";
                case "Terrain Tool": return "terrain";
                case "Upgrade Tool": return "upgrading";
                default: return "";
            }
        }

        public static void OnRemoteCursor(CursorCommand command)
        {
            // still joining: the list is cleared every frame until we're playing, so each update would
            // look like a new player (and print "joined")
            if (command == null || Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            if (!Inbound.InMap(command.X, command.Y, command.Z))
            {
                Inbound.Warn("cursor-position", $"Cursor from player {command.SenderId} is off the map; ignored");
                return;
            }

            bool joined = !Remote.TryGetValue(command.SenderId, out RemoteCursor cursor);
            if (joined)
            {
                if (Remote.Count >= MaxRemoteCursors)
                {
                    Inbound.Warn("cursor-count", "Too many player cursors; a new one was ignored");
                    return;
                }

                cursor = new RemoteCursor { Color = Palette[(Remote.Count + 1) % Palette.Length] };
                Remote[command.SenderId] = cursor;
            }

            string baseName = SafeName(command.Username, command.SenderId);
            string activity = command.Activity != null && Activities.Contains(command.Activity) ? command.Activity : "";
            if (joined || baseName != cursor.BaseName || activity != cursor.Activity)
            {
                cursor.BaseName = baseName;
                cursor.Activity = activity;
                RefreshNames();
            }

            cursor.Target = new float3(command.X, command.Y, command.Z);
            if (!cursor.Placed || !cursor.OnMap)
            {
                // first position, or coming back onto the map: jump there instead of sliding across
                cursor.Position = cursor.Target;
                cursor.Placed = true;
            }

            cursor.OnMap = command.OnMap;
            cursor.LastSeen = UnityEngine.Time.realtimeSinceStartup;
            if (joined)
            {
                CS2M.API.Chat.Instance?.PrintGameMessage($"{cursor.Name} joined");
            }
        }

        /// <summary>
        ///     The name to show for a remote player: control and format characters removed, at most
        ///     <see cref="MaxNameLength"/> characters, and never <see cref="ReservedName"/> or the local player's
        ///     own name (it would look like a message from the mod or from the local player).
        /// </summary>
        private static string SafeName(string username, int senderId)
        {
            string name = Inbound.CleanName(username, MaxNameLength);
            string local = Inbound.CleanName(Command.GetLocalUsername?.Invoke(), MaxNameLength);
            if (name.Length == 0 ||
                string.Equals(name, ReservedName, System.StringComparison.OrdinalIgnoreCase) ||
                (local.Length > 0 && string.Equals(name, local, System.StringComparison.OrdinalIgnoreCase)))
            {
                return $"Player {senderId}";
            }

            return name;
        }

        // Two players with the same name both show it with their id, except the host (id 0), so nobody can
        // pass for the host or for another player.
        private static void RefreshNames()
        {
            var counts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            foreach (RemoteCursor cursor in Remote.Values)
            {
                counts.TryGetValue(cursor.BaseName ?? "", out int count);
                counts[cursor.BaseName ?? ""] = count + 1;
            }

            foreach (KeyValuePair<int, RemoteCursor> pair in Remote)
            {
                string baseName = pair.Value.BaseName ?? "";
                pair.Value.Name = pair.Key != 0 && counts[baseName] > 1 ? $"{baseName} (#{pair.Key})" : baseName;
            }

            Version++;
        }

        private static void Smooth()
        {
            // frame-rate independent ease: covers ~90% of the gap in 0.15 s
            float t = 1f - math.exp(-UnityEngine.Time.unscaledDeltaTime * 15f);
            foreach (RemoteCursor cursor in Remote.Values)
            {
                cursor.Position = math.lerp(cursor.Position, cursor.Target, t);
            }
        }

        private static void DropStale()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            List<int> stale = null;
            foreach (KeyValuePair<int, RemoteCursor> pair in Remote)
            {
                if (now - pair.Value.LastSeen > Timeout)
                {
                    (stale ??= new List<int>()).Add(pair.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (int id in stale)
            {
                CS2M.API.Chat.Instance?.PrintGameMessage($"{Remote[id].Name} left");
                Remote.Remove(id);
            }

            RefreshNames();
        }
    }
}
