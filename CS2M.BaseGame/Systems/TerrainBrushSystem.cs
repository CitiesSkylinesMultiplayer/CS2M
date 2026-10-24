using System.Collections.Generic;
using Colossal.Mathematics;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Runs in the ApplyTool phase just before the game's ApplyBrushesSystem, so only on frames the
    ///     terrain tool applies (every frame while the mouse is held). Collects the temp brush stamps that
    ///     ApplyBrushesSystem is about to apply; TerrainBrushSendSystem sends them in batches.
    /// </summary>
    public partial class TerrainBrushCaptureSystem : GameSystemBase
    {
        private EntityQuery _tempBrushes;
        private ToolSystem _toolSystem;
        private PrefabSystem _prefabSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _tempBrushes = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Brush>(),
                ComponentType.ReadOnly<PrefabRef>());
            RequireForUpdate(_tempBrushes);
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null ||
                !(_toolSystem.activeTool is TerrainToolSystem))
            {
                return;
            }

            // ApplyTerrain uses this frame's delta time, so the receiver needs it to scale the stamp
            float dt = UnityEngine.Time.unscaledDeltaTime;
            using (NativeArray<Brush> brushes = _tempBrushes.ToComponentDataArray<Brush>(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = _tempBrushes.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < brushes.Length; i++)
                {
                    Brush brush = brushes[i];
                    if (!EntityManager.HasComponent<TerraformingData>(brush.m_Tool))
                    {
                        continue;
                    }

                    string tool = PrefabLookup.Name(_prefabSystem, brush.m_Tool);
                    string brushPrefab = PrefabLookup.Name(_prefabSystem, prefabs[i].m_Prefab);
                    if (tool == null || brushPrefab == null)
                    {
                        continue;
                    }

                    TerrainBrushSendSystem.Add(tool, brushPrefab, brush, dt);
                }
            }
        }
    }

    /// <summary>
    ///     Sends the collected stamps at most every ~100 ms (stamps happen every frame while the mouse is
    ///     held). Runs every frame in ToolUpdate so the last stamps of a stroke go out after release.
    /// </summary>
    public partial class TerrainBrushSendSystem : GameSystemBase
    {
        private const float BatchSeconds = 0.1f;

        private static readonly List<float> Stamps = new List<float>();
        private static string _tool;
        private static string _brush;
        private static float _firstStampAt;

        public static void Add(string tool, string brush, Brush b, float dt)
        {
            if (Stamps.Count > 0 && (tool != _tool || brush != _brush))
            {
                Flush();
            }

            if (Stamps.Count == 0)
            {
                _tool = tool;
                _brush = brush;
                _firstStampAt = UnityEngine.Time.realtimeSinceStartup;
            }

            Stamps.AddRange(new[]
            {
                b.m_Position.x, b.m_Position.y, b.m_Position.z,
                b.m_Target.x, b.m_Target.y, b.m_Target.z,
                b.m_Start.x, b.m_Start.y, b.m_Start.z,
                b.m_Angle, b.m_Size, b.m_Strength, b.m_Opacity, dt
            });
        }

        private static void Flush()
        {
            if (Stamps.Count == 0)
            {
                return;
            }

            if (Command.SendToAll != null && Command.CurrentRole != MultiplayerRole.None)
            {
                Command.SendToAll(new TerrainBrushCommand { Tool = _tool, Brush = _brush, Stamps = Stamps.ToArray() });
                BaseGameLog.Info($"Terrain sent: {Stamps.Count / TerrainBrushCommand.StampSize} stamp(s) of {_tool}");
            }

            Stamps.Clear();
        }

        protected override void OnUpdate()
        {
            if (Stamps.Count == 0)
            {
                return;
            }

            if (Command.CurrentRole == MultiplayerRole.None)
            {
                Stamps.Clear();
                return;
            }

            if (UnityEngine.Time.realtimeSinceStartup - _firstStampAt >= BatchSeconds)
            {
                Flush();
            }
        }
    }

    /// <summary>
    ///     Replays terrain stamps from other players through the same calls the game's ApplyBrushesSystem
    ///     makes (TerrainSystem.ApplyBrush for height, TerrainMaterialSystem.ApplyBrush for material).
    /// </summary>
    public partial class TerrainBrushApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<TerrainBrushCommand> Pending = new InboundQueue<TerrainBrushCommand>("TerrainBrushApplySystem", 4096, 16);

        // TerrainSystem.ApplyToTerrain's small-strength curve and delta clamp
        private const float SmallStrength = 0.002f;
        private const float SmallStrengthPower = 0.055f;

        // every stamp is a GPU dispatch
        private const int MaxStampsPerFrame = 256;

        private PrefabSystem _prefabSystem;
        private TerrainSystem _terrainSystem;
        private TerrainMaterialSystem _terrainMaterialSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            _terrainMaterialSystem = World.GetOrCreateSystemManaged<TerrainMaterialSystem>();
        }

        protected override void OnUpdate()
        {
            // received while joining: wait until the city has loaded
            if (Pending.IsEmpty || Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            // at most MaxStampsPerFrame stamps per frame; the rest waits for the next frame
            int stamps = 0;
            while (stamps < MaxStampsPerFrame && Pending.TryDequeue(out TerrainBrushCommand command))
            {
                try
                {
                    stamps += Apply(command);
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(TerrainBrushApplySystem), e);
                }
            }
        }

        // the handler checked the stamps: whole, finite, on the map, size and strength within the tool's range
        private int Apply(TerrainBrushCommand command)
        {
            Entity tool = PrefabLookup.Find(EntityManager, _prefabSystem, command.Tool);
            Entity brushPrefab = PrefabLookup.Find(EntityManager, _prefabSystem, command.Brush);
            if (tool == Entity.Null || brushPrefab == Entity.Null || !EntityManager.HasComponent<TerraformingData>(tool) ||
                !_prefabSystem.TryGetPrefab(brushPrefab, out BrushPrefab brushData))
            {
                Inbound.Warn("terrain-prefab", $"Terrain received with unknown tool or brush: {command.Tool}, {command.Brush}");
                return 0;
            }

            TerraformingData terraforming = EntityManager.GetComponentData<TerraformingData>(tool);
            float[] s = command.Stamps ?? new float[0];
            int count = 0;
            for (int i = 0; i + TerrainBrushCommand.StampSize <= s.Length; i += TerrainBrushCommand.StampSize)
            {
                var brush = new Brush
                {
                    m_Tool = tool,
                    m_Position = new float3(s[i], s[i + 1], s[i + 2]),
                    m_Target = new float3(s[i + 3], s[i + 4], s[i + 5]),
                    m_Start = new float3(s[i + 6], s[i + 7], s[i + 8]),
                    m_Angle = s[i + 9],
                    m_Size = s[i + 10],
                    m_Strength = s[i + 11],
                    m_Opacity = s[i + 12]
                };
                float senderDt = s[i + 13];

                switch (terraforming.m_Target)
                {
                    case TerraformingTarget.Height:
                        ApplyHeight(brush, brushData, terraforming.m_Type, senderDt);
                        count++;
                        break;
                    case TerraformingTarget.Material:
                        _terrainMaterialSystem.ApplyBrush(brush, brushPrefab);
                        count++;
                        break;
                }
            }

            BaseGameLog.Info($"Terrain received: {count} stamp(s) of {command.Tool}");
            return count;
        }

        // Same rules as ApplyBrushesSystem.ApplyHeight
        private void ApplyHeight(Brush brush, BrushPrefab brushPrefab, TerraformingType type, float senderDt)
        {
            if ((type == TerraformingType.Level || type == TerraformingType.Slope) && brush.m_Strength < 0f)
            {
                return;
            }

            if (type == TerraformingType.Soften && brush.m_Strength < 0f)
            {
                brush.m_Strength = math.abs(brush.m_Strength) * 2f;
            }

            brush.m_Strength = ScaleForFrameTime(brush.m_Strength, senderDt, UnityEngine.Time.unscaledDeltaTime);
            Bounds2 bounds = ToolUtils.GetBounds(brush);
            _terrainSystem.ApplyBrush(type, bounds, brush, brushPrefab.m_Texture);
        }

        /// <summary>
        ///     TerrainSystem scales each stamp by this frame's clamped delta time, so a stamp replayed on a
        ///     frame of a different length gets the strength that gives the sender's height change.
        /// </summary>
        private static float ScaleForFrameTime(float strength, float senderDt, float localDt)
        {
            float sender = math.clamp(senderDt, 0.05f, 1f);
            float local = math.clamp(localDt, 0.05f, 1f);
            if (sender == local || strength == 0f)
            {
                return strength;
            }

            float effective = Curve(strength) * sender / local;
            return InverseCurve(effective);
        }

        private static float Curve(float strength)
        {
            float a = math.abs(strength);
            return a < SmallStrength
                ? math.pow(a / SmallStrength, SmallStrengthPower) * SmallStrength * math.sign(strength)
                : strength;
        }

        private static float InverseCurve(float value)
        {
            float a = math.abs(value);
            return a < SmallStrength
                ? math.pow(a / SmallStrength, 1f / SmallStrengthPower) * SmallStrength * math.sign(value)
                : value;
        }
    }
}
