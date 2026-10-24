using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;
using WaterSourceData = Game.Simulation.WaterSourceData;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Runs in the ApplyTool phase just before the game's ApplyWaterSourcesSystem, so only on the frame
    ///     the water tool applies. Each temp water source there is exactly what will be created, changed
    ///     or removed, with the source it replaces still at its old position.
    /// </summary>
    public partial class WaterSourceCaptureSystem : GameSystemBase
    {
        private EntityQuery _tempSources;
        private ToolSystem _toolSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            // Same query as ApplyWaterSourcesSystem: tool-made sources have no PrefabRef
            _tempSources = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<WaterSourceData>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<PrefabRef>());
            RequireForUpdate(_tempSources);
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null ||
                !(_toolSystem.activeTool is WaterToolSystem))
            {
                return;
            }

            var changes = new List<WaterSourceChange>();
            using (NativeArray<Entity> entities = _tempSources.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    Temp temp = EntityManager.GetComponentData<Temp>(entity);
                    if ((temp.m_Flags & TempFlags.Cancel) != 0)
                    {
                        continue;
                    }

                    WaterSourceData data = EntityManager.GetComponentData<WaterSourceData>(entity);
                    Transform transform = EntityManager.GetComponentData<Transform>(entity);
                    bool hasOriginal = temp.m_Original != Entity.Null &&
                                       EntityManager.HasComponent<WaterSourceData>(temp.m_Original) &&
                                       EntityManager.HasComponent<Transform>(temp.m_Original);
                    float3 oldPosition = hasOriginal
                        ? EntityManager.GetComponentData<Transform>(temp.m_Original).m_Position
                        : float3.zero;

                    int kind;
                    if ((temp.m_Flags & TempFlags.Delete) != 0)
                    {
                        if (!hasOriginal)
                        {
                            continue;
                        }

                        kind = 2;
                    }
                    else if (hasOriginal)
                    {
                        // a hovered/selected source is re-applied unchanged; nothing to send
                        if (Same(EntityManager.GetComponentData<WaterSourceData>(temp.m_Original), data) &&
                            math.distancesq(oldPosition, transform.m_Position) < 0.0001f)
                        {
                            continue;
                        }

                        kind = 1;
                    }
                    else
                    {
                        kind = 0;
                    }

                    changes.Add(new WaterSourceChange
                    {
                        Kind = kind,
                        OldPosition = NetRefs.F(oldPosition),
                        Position = NetRefs.F(transform.m_Position),
                        ConstantDepth = data.m_ConstantDepth,
                        Radius = data.m_Radius,
                        Height = data.m_Height,
                        Multiplier = data.m_Multiplier,
                        Polluted = data.m_Polluted,
                        Modifier = data.m_Modifier
                    });
                }
            }

            if (changes.Count == 0)
            {
                return;
            }

            Command.SendToAll(new WaterSourceCommand { Changes = changes.ToArray() });
            foreach (WaterSourceChange change in changes)
            {
                BaseGameLog.Info($"Water source sent: {KindName(change.Kind)} at {NetRefs.V3(change.Kind == 0 ? change.Position : change.OldPosition)}" +
                                 $" (radius {change.Radius}, height {change.Height})");
            }
        }

        private static bool Same(WaterSourceData a, WaterSourceData b)
        {
            return a.m_ConstantDepth == b.m_ConstantDepth && a.m_Radius == b.m_Radius && a.m_Height == b.m_Height &&
                   a.m_Multiplier == b.m_Multiplier && a.m_Polluted == b.m_Polluted && a.m_Modifier == b.m_Modifier;
        }

        internal static string KindName(int kind) => kind == 0 ? "created" : kind == 1 ? "changed" : "removed";
    }

    /// <summary>
    ///     Makes the same entity changes as the game's ApplyWaterSourcesSystem for water sources other
    ///     players created, changed or removed.
    /// </summary>
    public partial class WaterSourceApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<WaterSourceCommand> Pending = new InboundQueue<WaterSourceCommand>("WaterSourceApplySystem", 256, 16);

        // Sources are found by their horizontal position; the water simulation may adjust the height
        private const float MatchDistanceSq = 1f;

        private EntityQuery _sources;
        private WaterSystem _waterSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _waterSystem = World.GetOrCreateSystemManaged<WaterSystem>();
            _sources = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<WaterSourceData>(), ComponentType.ReadOnly<Transform>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PrefabRef>() }
            });
        }

        protected override void OnUpdate()
        {
            // received while joining: wait until the city has loaded
            if (Pending.IsEmpty || Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            while (Pending.TryDequeue(out WaterSourceCommand command))
            {
                try
                {
                    var buffer = new EntityCommandBuffer(Allocator.Temp);
                    var claimed = new HashSet<Entity>();
                    foreach (WaterSourceChange change in command.Changes ?? new WaterSourceChange[0])
                    {
                        Apply(buffer, change, claimed);
                    }

                    buffer.Playback(EntityManager);
                    buffer.Dispose();
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(WaterSourceApplySystem), e);
                }
            }
        }

        private void Apply(EntityCommandBuffer buffer, WaterSourceChange change, HashSet<Entity> claimed)
        {
            var transform = new Transform { m_Position = NetRefs.V3(change.Position), m_Rotation = quaternion.identity };
            if (change.Kind == 0)
            {
                Entity entity = buffer.CreateEntity();
                buffer.AddComponent(entity, ToData(change, _waterSystem.GetNextSourceId()));
                buffer.AddComponent(entity, transform);
                buffer.AddComponent<Created>(entity);
                buffer.AddComponent<Updated>(entity);
                BaseGameLog.Info($"Water source received: created at {transform.m_Position}");
                return;
            }

            Entity target = Find(NetRefs.V3(change.OldPosition), claimed);
            if (target == Entity.Null)
            {
                BaseGameLog.Warn($"Water source received ({WaterSourceCaptureSystem.KindName(change.Kind)}) for a source that doesn't exist here at {NetRefs.V3(change.OldPosition)}");
                return;
            }

            claimed.Add(target);
            if (change.Kind == 2)
            {
                buffer.AddComponent<Deleted>(target);
                BaseGameLog.Info($"Water source received: removed at {NetRefs.V3(change.OldPosition)}");
                return;
            }

            // keep the local id: ids are per game (the water tool's UI refers to them)
            int id = EntityManager.GetComponentData<WaterSourceData>(target).m_Id;
            buffer.SetComponent(target, ToData(change, id));
            buffer.SetComponent(target, transform);
            buffer.AddComponent<Updated>(target);
            BaseGameLog.Info($"Water source received: changed at {NetRefs.V3(change.OldPosition)} -> {transform.m_Position} (radius {change.Radius}, height {change.Height})");
        }

        private static WaterSourceData ToData(WaterSourceChange change, int id)
        {
            return new WaterSourceData
            {
                m_ConstantDepth = change.ConstantDepth,
                m_Radius = change.Radius,
                m_Height = change.Height,
                m_Multiplier = change.Multiplier,
                m_Polluted = change.Polluted,
                m_Modifier = change.Modifier,
                m_Id = id
            };
        }

        private Entity Find(float3 position, HashSet<Entity> claimed)
        {
            Entity best = Entity.Null;
            float bestDistance = MatchDistanceSq;
            using (NativeArray<Entity> entities = _sources.ToEntityArray(Allocator.Temp))
            using (NativeArray<Transform> transforms = _sources.ToComponentDataArray<Transform>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    float d = math.distancesq(transforms[i].m_Position.xz, position.xz);
                    if (d < bestDistance && !claimed.Contains(entities[i]))
                    {
                        best = entities[i];
                        bestDistance = d;
                    }
                }
            }

            return best;
        }
    }
}
