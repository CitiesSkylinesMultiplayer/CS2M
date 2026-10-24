using System.Collections.Generic;
using System.Reflection;
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
    ///     Converts tool definitions to and from DefinitionData.
    /// </summary>
    internal static class Definitions
    {
        public static float[] Q(quaternion q) => new[] { q.value.x, q.value.y, q.value.z, q.value.w };

        public static quaternion Q(float[] q) =>
            q != null && q.Length >= 4 ? new quaternion(q[0], q[1], q[2], q[3]) : quaternion.identity;

        public static DefinitionData Describe(EntityManager em, PrefabSystem prefabs, Entity entity)
        {
            CreationDefinition c = em.GetComponentData<CreationDefinition>(entity);
            var data = new DefinitionData
            {
                Prefab = c.m_Prefab != Entity.Null ? PrefabLookup.Name(prefabs, c.m_Prefab) : "",
                SubPrefab = c.m_SubPrefab != Entity.Null ? PrefabLookup.Name(prefabs, c.m_SubPrefab) : "",
                CreationFlags = (uint)c.m_Flags,
                RandomSeed = c.m_RandomSeed,
                Original = NetRefs.Describe(em, c.m_Original, prefabs),
                HadOriginal = c.m_Original != Entity.Null,
                Owner = NetRefs.Describe(em, c.m_Owner, prefabs)
            };
            if (c.m_Attached != Entity.Null)
            {
                if (em.HasComponent<PrefabData>(c.m_Attached))
                {
                    data.AttachedPrefab = PrefabLookup.Name(prefabs, c.m_Attached);
                }
                else
                {
                    data.Attached = NetRefs.Describe(em, c.m_Attached, prefabs);
                }
            }

            if (em.HasComponent<ObjectDefinition>(entity))
            {
                ObjectDefinition o = em.GetComponentData<ObjectDefinition>(entity);
                data.Object = new SpawnCommand
                {
                    Position = NetRefs.F(o.m_Position), LocalPosition = NetRefs.F(o.m_LocalPosition),
                    Scale = NetRefs.F(o.m_Scale), Rotation = Q(o.m_Rotation), LocalRotation = Q(o.m_LocalRotation),
                    Elevation = o.m_Elevation, Intensity = o.m_Intensity, Age = o.m_Age,
                    IsDecoration = o.m_IsDecoration, ParentMesh = o.m_ParentMesh, GroupIndex = o.m_GroupIndex,
                    Probability = o.m_Probability, PrefabSubIndex = o.m_PrefabSubIndex
                };
            }

            if (em.HasComponent<OwnerDefinition>(entity))
            {
                OwnerDefinition owner = em.GetComponentData<OwnerDefinition>(entity);
                data.HasOwnerDefinition = true;
                data.OwnerPrefab = PrefabLookup.Name(prefabs, owner.m_Prefab);
                data.OwnerPosition = NetRefs.F(owner.m_Position);
                data.OwnerRotation = Q(owner.m_Rotation);
            }

            if (em.HasComponent<NetCourse>(entity))
            {
                NetCourse course = em.GetComponentData<NetCourse>(entity);
                Bezier4x3 b = course.m_Curve;
                data.Course = new NetCourseData
                {
                    Curve = new[] { b.a.x, b.a.y, b.a.z, b.b.x, b.b.y, b.b.z, b.c.x, b.c.y, b.c.z, b.d.x, b.d.y, b.d.z },
                    Elevation = new[] { course.m_Elevation.x, course.m_Elevation.y },
                    Length = course.m_Length,
                    FixedIndex = course.m_FixedIndex,
                    Start = RoadCaptureSystem.Pos(em, course.m_StartPosition),
                    End = RoadCaptureSystem.Pos(em, course.m_EndPosition)
                };
            }

            if (em.HasBuffer<Game.Areas.Node>(entity))
            {
                DynamicBuffer<Game.Areas.Node> nodes = em.GetBuffer<Game.Areas.Node>(entity, true);
                var flat = new float[nodes.Length * 4];
                for (int i = 0; i < nodes.Length; i++)
                {
                    flat[i * 4] = nodes[i].m_Position.x;
                    flat[i * 4 + 1] = nodes[i].m_Position.y;
                    flat[i * 4 + 2] = nodes[i].m_Position.z;
                    flat[i * 4 + 3] = nodes[i].m_Elevation;
                }

                data.AreaNodes = flat;
            }

            return data;
        }

        /// <summary>
        ///     Queues a permanent definition (created Deleted, so the game's generators build it this frame).
        /// </summary>
        public static bool Create(EntityManager em, PrefabSystem prefabs, EntityCommandBuffer buffer, DefinitionData data)
        {
            Entity prefab = string.IsNullOrEmpty(data.Prefab) ? Entity.Null : PrefabLookup.Find(em, prefabs, data.Prefab);
            if (!string.IsNullOrEmpty(data.Prefab) && prefab == Entity.Null)
            {
                BaseGameLog.Warn($"Placement received with an unknown prefab {data.Prefab}");
                return false;
            }

            Entity original = Entity.Null, owner = Entity.Null, attached = Entity.Null;
            if ((data.Original != null && data.Original.Kind != 0 &&
                 (!NetRefs.Resolve(em, data.Original, out original, prefabs) || original == Entity.Null)) ||
                (data.Owner != null && data.Owner.Kind != 0 &&
                 (!NetRefs.Resolve(em, data.Owner, out owner, prefabs) || owner == Entity.Null)))
            {
                BaseGameLog.Warn($"Placement received for something missing here ({data.Prefab})");
                return false;
            }

            if (!string.IsNullOrEmpty(data.AttachedPrefab))
            {
                attached = PrefabLookup.Find(em, prefabs, data.AttachedPrefab);
            }
            else if (data.Attached != null)
            {
                NetRefs.Resolve(em, data.Attached, out attached, prefabs);
            }

            Entity entity = buffer.CreateEntity();
            buffer.AddComponent(entity, new CreationDefinition
            {
                m_Prefab = prefab,
                m_SubPrefab = string.IsNullOrEmpty(data.SubPrefab) ? Entity.Null : PrefabLookup.Find(em, prefabs, data.SubPrefab),
                m_Original = original,
                m_Owner = owner,
                m_Attached = attached,
                m_Flags = (CreationFlags)data.CreationFlags | CreationFlags.Permanent,
                m_RandomSeed = data.RandomSeed
            });

            if (data.Object != null)
            {
                SpawnCommand o = data.Object;
                buffer.AddComponent(entity, new ObjectDefinition
                {
                    m_Position = NetRefs.V3(o.Position), m_LocalPosition = NetRefs.V3(o.LocalPosition),
                    m_Scale = NetRefs.V3(o.Scale), m_Rotation = Q(o.Rotation), m_LocalRotation = Q(o.LocalRotation),
                    m_Elevation = o.Elevation, m_Intensity = o.Intensity, m_Age = o.Age,
                    m_IsDecoration = o.IsDecoration, m_ParentMesh = o.ParentMesh, m_GroupIndex = o.GroupIndex,
                    m_Probability = o.Probability, m_PrefabSubIndex = o.PrefabSubIndex
                });
            }

            if (data.HasOwnerDefinition)
            {
                buffer.AddComponent(entity, new OwnerDefinition
                {
                    m_Prefab = PrefabLookup.Find(em, prefabs, data.OwnerPrefab),
                    m_Position = NetRefs.V3(data.OwnerPosition),
                    m_Rotation = Q(data.OwnerRotation)
                });
            }

            if (data.Course != null)
            {
                float[] c = data.Course.Curve;
                buffer.AddComponent(entity, new NetCourse
                {
                    m_Curve = new Bezier4x3(new float3(c[0], c[1], c[2]), new float3(c[3], c[4], c[5]),
                        new float3(c[6], c[7], c[8]), new float3(c[9], c[10], c[11])),
                    m_Elevation = new float2(data.Course.Elevation[0], data.Course.Elevation[1]),
                    m_Length = data.Course.Length,
                    m_FixedIndex = data.Course.FixedIndex,
                    m_StartPosition = RoadApplySystem.Pos(em, data.Course.Start, joinExisting: true),
                    m_EndPosition = RoadApplySystem.Pos(em, data.Course.End, joinExisting: true)
                });
            }

            if (data.AreaNodes != null)
            {
                DynamicBuffer<Game.Areas.Node> nodes = buffer.AddBuffer<Game.Areas.Node>(entity);
                for (int i = 0; i + 3 < data.AreaNodes.Length; i += 4)
                {
                    float[] n = data.AreaNodes;
                    nodes.Add(new Game.Areas.Node(new float3(n[i], n[i + 1], n[i + 2]), n[i + 3]));
                }
            }

            buffer.AddComponent<Updated>(entity);
            buffer.AddComponent<Deleted>(entity);
            return true;
        }
    }

    /// <summary>
    ///     Runs in the ApplyTool phase before the game's apply systems. While the building/prop tool is active,
    ///     every definition (the building and its owned roads and areas) is sent.
    /// </summary>
    public partial class PlaceCaptureSystem : GameSystemBase
    {
        /// <summary>Set when this frame's placement went out from definitions (read and reset by LotBuildingCaptureSystem).</summary>
        public static bool SentThisFrame;

        private static readonly FieldInfo LastTool =
            typeof(ToolSystem).GetField("m_LastTool", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        ///     The tool whose apply is running (ToolSystem.applyMode reads the same field); activeTool can already
        ///     be the next tool by then.
        /// </summary>
        public static ToolBaseSystem ApplyingTool(ToolSystem toolSystem) =>
            LastTool?.GetValue(toolSystem) as ToolBaseSystem ?? toolSystem.activeTool;

        private EntityQuery _definitions;
        private ToolSystem _toolSystem;
        private PrefabSystem _prefabSystem;
        private string _lastKey;
        private float _lastTime;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _definitions = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>());
            RequireForUpdate(_definitions);
        }

        protected override void OnUpdate()
        {
            // a move switches back to the default tool during the click, so ask which tool is applying
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null ||
                !(ApplyingTool(_toolSystem) is ObjectToolSystem))
            {
                return;
            }

            var definitions = new List<DefinitionData>();
            using (NativeArray<Entity> entities = _definitions.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if ((EntityManager.GetComponentData<CreationDefinition>(entity).m_Flags & CreationFlags.Permanent) == 0)
                    {
                        definitions.Add(Definitions.Describe(EntityManager, _prefabSystem, entity));
                    }
                }
            }

            if (definitions.Count == 0)
            {
                return;
            }

            DefinitionData main = definitions.Find(d => d.Object != null && !d.HasOwnerDefinition) ?? definitions[0];
            // every prefab and position is in the key: upgrades placed one after another share the same main (owner) definition
            string key = string.Join("|", definitions.ConvertAll(d => d.Prefab + string.Join(",", d.Object?.Position ?? new float[0])));
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (key == _lastKey && now - _lastTime < 0.5f)
            {
                return;
            }

            _lastKey = key;
            _lastTime = now;
            Command.SendToAll(new PlaceCommand { Definitions = definitions.ToArray() });
            SentThisFrame = true;
            BaseGameLog.Info($"Placement sent: {main.Prefab} with {definitions.Count - 1} part(s)");
        }
    }

    /// <summary>
    ///     Builds what other players placed with the building/prop tool.
    /// </summary>
    public partial class PlaceApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<PlaceCommand> Pending = new InboundQueue<PlaceCommand>("PlaceApplySystem", 1024, 16);
        private PrefabSystem _prefabSystem;

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

            while (Pending.TryDequeue(out PlaceCommand command))
            {
                try
                {
                    var buffer = new EntityCommandBuffer(Allocator.Temp);
                    DefinitionData[] definitions = command.Definitions ?? new DefinitionData[0];

                    // a permanent definition with an original would build a copy and hide the original, so
                    // moves, upgrades and removals of existing things are applied to them directly
                    int changed = ExistingParts.Apply(EntityManager, _prefabSystem, buffer, definitions);
                    int built = 0;
                    foreach (DefinitionData data in definitions)
                    {
                        if (!data.HadOriginal && Definitions.Create(EntityManager, _prefabSystem, buffer, data))
                        {
                            built++;
                        }
                    }

                    buffer.Playback(EntityManager);
                    buffer.Dispose();
                    BaseGameLog.Info($"Placement received: {built} built, {changed} existing changed, of {definitions.Length} definition(s)");
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(PlaceApplySystem), e);
                }
            }
        }
    }
}
