using System.Linq;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.UI.InGame;
using Unity.Entities;
using UnityEngine;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Custom colours are set from the selected object's info panel, so the selected object is checked
    ///     4 times a second and its CustomMeshColor sent when it changes. ("Apply to all of this type" is not covered.)
    /// </summary>
    public partial class ColorSyncSystem : GameSystemBase
    {
        public static readonly InboundQueue<ColorCommand> Pending = new InboundQueue<ColorCommand>("ColorSyncSystem", 2048, 64);

        private const float CheckInterval = 0.25f;

        private SelectedInfoUISystem _selected;
        private PrefabSystem _prefabSystem;
        private Entity _watched;
        private string _known;
        private float _lastCheck;

        protected override void OnCreate()
        {
            base.OnCreate();
            _selected = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                _watched = Entity.Null;
                return;
            }

            while (Pending.TryDequeue(out ColorCommand command))
            {
                try
                {
                    Apply(command);
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(ColorSyncSystem), e);
                }
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastCheck < CheckInterval)
            {
                return;
            }

            _lastCheck = now;
            Entity entity = _selected.selectedEntity;
            if (entity == Entity.Null || !EntityManager.Exists(entity))
            {
                _watched = Entity.Null;
                return;
            }

            string state = State(entity, out bool enabled, out float[] colors);
            if (entity != _watched)
            {
                // newly selected: remember, don't send
                _watched = entity;
                _known = state;
                return;
            }

            if (state == _known)
            {
                return;
            }

            _known = state;
            NetRef target = NetRefs.Describe(EntityManager, entity, _prefabSystem);
            if (target.Kind == 0)
            {
                return;
            }

            Command.SendToAll?.Invoke(new ColorCommand { Target = target, Enabled = enabled, Colors = colors });
            BaseGameLog.Info($"Colour sent for {target.Prefab ?? "a road"}");
        }

        private string State(Entity entity, out bool enabled, out float[] colors)
        {
            enabled = false;
            colors = new float[0];
            if (!EntityManager.HasBuffer<CustomMeshColor>(entity))
            {
                return "none";
            }

            enabled = EntityManager.IsComponentEnabled<CustomMeshColor>(entity);
            DynamicBuffer<CustomMeshColor> buffer = EntityManager.GetBuffer<CustomMeshColor>(entity, true);
            colors = new float[buffer.Length * 12];
            for (int i = 0; i < buffer.Length; i++)
            {
                ColorSet set = buffer[i].m_ColorSet;
                Color[] channels = { set.m_Channel0, set.m_Channel1, set.m_Channel2 };
                for (int c = 0; c < 3; c++)
                {
                    colors[i * 12 + c * 4] = channels[c].r;
                    colors[i * 12 + c * 4 + 1] = channels[c].g;
                    colors[i * 12 + c * 4 + 2] = channels[c].b;
                    colors[i * 12 + c * 4 + 3] = channels[c].a;
                }
            }

            return enabled + ":" + string.Join(",", colors.Select(f => f.ToString("0.###")));
        }

        private void Apply(ColorCommand command)
        {
            if (!NetRefs.Resolve(EntityManager, command.Target, out Entity entity, _prefabSystem) || entity == Entity.Null)
            {
                BaseGameLog.Warn($"Colour received for something that isn't here ({command.Target?.Prefab})");
                return;
            }

            float[] c = command.Colors ?? new float[0];
            if (c.Length >= 12)
            {
                DynamicBuffer<CustomMeshColor> buffer = EntityManager.HasBuffer<CustomMeshColor>(entity)
                    ? EntityManager.GetBuffer<CustomMeshColor>(entity)
                    : EntityManager.AddBuffer<CustomMeshColor>(entity);
                buffer.Clear();
                for (int i = 0; i + 11 < c.Length; i += 12)
                {
                    var set = new ColorSet
                    {
                        m_Channel0 = new Color(c[i], c[i + 1], c[i + 2], c[i + 3]),
                        m_Channel1 = new Color(c[i + 4], c[i + 5], c[i + 6], c[i + 7]),
                        m_Channel2 = new Color(c[i + 8], c[i + 9], c[i + 10], c[i + 11])
                    };
                    buffer.Add(new CustomMeshColor { m_ColorSet = set });
                }
            }

            if (EntityManager.HasBuffer<CustomMeshColor>(entity))
            {
                EntityManager.SetComponentEnabled<CustomMeshColor>(entity, command.Enabled);
            }

            if (!EntityManager.HasComponent<BatchesUpdated>(entity))
            {
                EntityManager.AddComponent<BatchesUpdated>(entity);
            }

            // the next check of a selected object must not send this straight back
            if (entity == _watched)
            {
                _known = State(entity, out _, out _);
            }
        }
    }
}
