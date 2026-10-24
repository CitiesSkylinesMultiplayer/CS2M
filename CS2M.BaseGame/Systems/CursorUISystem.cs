using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using CS2M.API.Commands;
using Game.Rendering;
using Game.UI;
using Unity.Mathematics;
using UnityEngine;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Gives the UI the screen position of each other player's cursor, for the name labels.
    ///     Positions are percentages of the screen so they work at any resolution and UI scale.
    /// </summary>
    public partial class CursorUISystem : UISystemBase
    {
        private CameraUpdateSystem _cameraUpdateSystem;
        private ValueBinding<string> _cursors;
        private string _last = "[]";
        private readonly StringBuilder _cursorJson = new StringBuilder();

        // each cursor's name, activity and colour, escaped once per change instead of every frame
        private readonly Dictionary<CursorSystem.RemoteCursor, string> _labels = new Dictionary<CursorSystem.RemoteCursor, string>();
        private readonly Dictionary<CursorSystem.RemoteCursor, string> _colours = new Dictionary<CursorSystem.RemoteCursor, string>();
        private int _labelsVersion = -1;

        // Players for the multiplayer HUD: [{"n": name, "c": colour, "a": activity, "me": bool}], empty when not in a session
        private ValueBinding<string> _players;
        private string _lastPlayers = "[]";
        private int _playersVersion = -1;
        private MultiplayerRole _playersRole = MultiplayerRole.None;
        private string _playersLocalName;

        protected override void OnCreate()
        {
            base.OnCreate();
            _cameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            AddBinding(_cursors = new ValueBinding<string>("CS2M", "Cursors", "[]"));
            AddBinding(_players = new ValueBinding<string>("CS2M", "Players", "[]"));
        }

        protected override void OnUpdate()
        {
            UpdatePlayers();
            UpdateLabels();

            // positions move every frame while a cursor glides or the camera turns, so this one is built each
            // frame, from cached pieces, and only turned into a string when it differs from the last one
            _cursorJson.Clear().Append('[');
            if (CursorSystem.Remote.Count != 0 && _cameraUpdateSystem.TryGetViewer(out Viewer viewer))
            {
                foreach (CursorSystem.RemoteCursor cursor in CursorSystem.Remote.Values)
                {
                    if (!cursor.OnMap)
                    {
                        continue;
                    }

                    Vector3 screen = viewer.camera.WorldToScreenPoint(cursor.Position);
                    if (screen.z <= 0f)
                    {
                        continue;
                    }

                    if (_cursorJson.Length > 1)
                    {
                        _cursorJson.Append(',');
                    }

                    _cursorJson.Append(_labels.TryGetValue(cursor, out string label) ? label : "{\"n\":\"\",\"a\":\"\"")
                        .Append(",\"x\":").Append(Pct(screen.x / Screen.width))
                        .Append(",\"y\":").Append(Pct(1f - screen.y / Screen.height))
                        .Append(",\"c\":\"#").Append(_colours.TryGetValue(cursor, out string colour) ? colour : "FFFFFF").Append("\"}");
                }
            }

            _cursorJson.Append(']');
            if (!Same(_cursorJson, _last))
            {
                _last = _cursorJson.ToString();
                _cursors.Update(_last);
            }
        }

        private void UpdateLabels()
        {
            if (_labelsVersion == CursorSystem.Version)
            {
                return;
            }

            _labelsVersion = CursorSystem.Version;
            _labels.Clear();
            _colours.Clear();
            foreach (CursorSystem.RemoteCursor cursor in CursorSystem.Remote.Values)
            {
                _labels[cursor] = "{\"n\":\"" + Escape(cursor.Name) + "\",\"a\":\"" + Escape(cursor.Activity) + "\"";
                _colours[cursor] = ColorUtility.ToHtmlStringRGB(cursor.Color);
            }
        }

        private void UpdatePlayers()
        {
            MultiplayerRole role = Command.CurrentRole;
            string localName = role == MultiplayerRole.None ? null : Command.GetLocalUsername?.Invoke();
            if (_playersVersion == CursorSystem.Version && _playersRole == role && _playersLocalName == localName)
            {
                return;
            }

            _playersVersion = CursorSystem.Version;
            _playersRole = role;
            _playersLocalName = localName;

            string json = "[]";
            if (role != MultiplayerRole.None)
            {
                string suffix = role == MultiplayerRole.Server ? " (host)" : "";
                var sb = new StringBuilder("[");
                sb.Append("{\"n\":\"").Append(Escape((localName ?? "You") + suffix))
                    .Append("\",\"c\":\"#D6FF3F\",\"a\":\"\",\"me\":true}");
                foreach (KeyValuePair<int, CursorSystem.RemoteCursor> pair in CursorSystem.Remote)
                {
                    CursorSystem.RemoteCursor cursor = pair.Value;
                    sb.Append(",{\"id\":").Append(pair.Key).Append(",\"n\":\"").Append(Escape(cursor.Name))
                        .Append("\",\"c\":\"#").Append(ColorUtility.ToHtmlStringRGB(cursor.Color))
                        .Append("\",\"a\":\"").Append(Escape(cursor.Activity)).Append("\",\"me\":false}");
                }

                json = sb.Append(']').ToString();
            }

            if (json != _lastPlayers)
            {
                _players.Update(json);
                _lastPlayers = json;
            }
        }

        private static bool Same(StringBuilder sb, string text)
        {
            if (sb.Length != text.Length)
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (sb[i] != text[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string Pct(float value) =>
            (math.clamp(value, -1f, 2f) * 100f).ToString("0.##", CultureInfo.InvariantCulture);

        private static string Escape(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? "")
            {
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\').Append(c);
                }
                else if (c >= ' ')
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
