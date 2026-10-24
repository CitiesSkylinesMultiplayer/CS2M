using Colossal.Mathematics;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Builds roads received from other players. Each course becomes a permanent definition
    ///     (created already Deleted, like the game's own zone spawning does), so the game's road
    ///     generators build the real nodes and edges this frame, splits and zone blocks included.
    /// </summary>
    public partial class RoadApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<RoadCommand> Pending = new InboundQueue<RoadCommand>("RoadApplySystem", 1024, 16);

        private PrefabSystem _prefabSystem;
        private EntityArchetype _definitionArchetype;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _definitionArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<CreationDefinition>(),
                ComponentType.ReadWrite<NetCourse>(),
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

            while (Pending.TryDequeue(out RoadCommand command))
            {
                try
                {
                    int built = 0;
                    var buffer = new EntityCommandBuffer(Allocator.Temp);
                    foreach (NetCourseData data in command.Courses ?? new NetCourseData[0])
                    {
                        Entity original = Entity.Null;
                        if (data.Original != null && data.Original.Kind != 0 &&
                            (!NetRefs.Resolve(EntityManager, data.Original, out original) || original == Entity.Null))
                        {
                            BaseGameLog.Warn($"Road received that rebuilds a road missing here at {NetRefs.V3(data.Original.A)}");
                            continue;
                        }

                        Entity prefab = string.IsNullOrEmpty(data.Prefab) ? Entity.Null : PrefabLookup.Find(EntityManager, _prefabSystem, data.Prefab);
                        if ((prefab == Entity.Null && (original == Entity.Null || !string.IsNullOrEmpty(data.Prefab))) ||
                            (prefab != Entity.Null && !EntityManager.HasComponent<NetData>(prefab)))
                        {
                            BaseGameLog.Warn($"Road received with an unknown prefab {data.Prefab}");
                            continue;
                        }

                        var definition = new CreationDefinition
                        {
                            m_Prefab = prefab,
                            m_Original = original,
                            m_Flags = (CreationFlags)data.CreationFlags | CreationFlags.Permanent,
                            m_RandomSeed = data.RandomSeed
                        };

                        float[] c = data.Curve;
                        var course = new NetCourse
                        {
                            m_Curve = new Bezier4x3(new float3(c[0], c[1], c[2]), new float3(c[3], c[4], c[5]),
                                new float3(c[6], c[7], c[8]), new float3(c[9], c[10], c[11])),
                            m_Elevation = new float2(data.Elevation[0], data.Elevation[1]),
                            m_Length = data.Length,
                            m_FixedIndex = data.FixedIndex,
                            m_StartPosition = Pos(EntityManager, data.Start),
                            m_EndPosition = Pos(EntityManager, data.End)
                        };

                        Entity entity = buffer.CreateEntity(_definitionArchetype);
                        buffer.SetComponent(entity, definition);
                        buffer.SetComponent(entity, course);
                        built++;
                    }

                    buffer.Playback(EntityManager);
                    buffer.Dispose();

                    BaseGameLog.Info($"Road received: {built} of {command.Courses?.Length ?? 0} course(s) built");
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(RoadApplySystem), e);
                }
            }
        }

        /// <param name="joinExisting">
        ///     A building's own pipes/roads/power lines are defined with free ends. If a junction already sits at
        ///     exactly that spot here (e.g. the pipe arrived before the building), join it, or the two stay
        ///     unconnected and the simulation treats the building as cut off.
        /// </param>
        public static CoursePos Pos(EntityManager em, CoursePosData data, bool joinExisting = false)
        {
            if (!NetRefs.Resolve(em, data.Entity, out Entity entity))
            {
                // the junction or road it snapped to isn't here; the road still gets built, unconnected
                BaseGameLog.Warn($"Road end connects to something missing here at {NetRefs.V3(data.Position)}");
            }

            if (joinExisting && entity == Entity.Null && (data.Entity == null || data.Entity.Kind == 0))
            {
                NetRefs.Resolve(em, new NetRef { Kind = 1, A = data.Position }, out entity);
            }

            return new CoursePos
            {
                m_Entity = entity,
                m_Position = NetRefs.V3(data.Position),
                m_Rotation = new quaternion(data.Rotation[0], data.Rotation[1], data.Rotation[2], data.Rotation[3]),
                m_Elevation = new float2(data.Elevation[0], data.Elevation[1]),
                m_CourseDelta = data.CourseDelta,
                m_SplitPosition = data.SplitPosition,
                m_Flags = (CoursePosFlags)data.Flags,
                m_ParentMesh = data.ParentMesh
            };
        }
    }
}
