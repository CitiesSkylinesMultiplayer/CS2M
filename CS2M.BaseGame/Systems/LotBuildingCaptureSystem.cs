using System.Collections.Generic;
using Colossal.Mathematics;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     ApplyTool phase: notes that the building or area tool applied this frame. (The building tool switches
    ///     to the area tool during the click for drawn-lot buildings, so either can be active by now.)
    /// </summary>
    public partial class LotBuildingMarkerSystem : GameSystemBase
    {
        public static bool ToolApplied;

        private ToolSystem _toolSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        }

        protected override void OnUpdate()
        {
            ToolBaseSystem tool = PlaceCaptureSystem.ApplyingTool(_toolSystem);
            if (tool is AreaToolSystem || tool is ObjectToolSystem)
            {
                ToolApplied = true;
            }
        }
    }

    /// <summary>
    ///     Buildings with a drawn lot (landfill and the like): the building tool leaves the building as a preview
    ///     and hands over to the area tool, whose apply turns the leftover preview into the real building. No
    ///     building definition exists by then, so the building is sent from the result, right after the
    ///     ToolOutputBarrier: its prefab and transform, plus its lot/surface areas and internal roads as owned
    ///     definitions. The area sync skips those areas (their owner is Created this frame).
    /// </summary>
    public partial class LotBuildingCaptureSystem : GameSystemBase
    {
        /// <summary>Set when this frame sent a building from the result; its new areas went with it.</summary>
        public static bool SentThisFrame;

        private PrefabSystem _prefabSystem;
        private EntityQuery _createdBuildings;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _createdBuildings = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Objects.Transform>()
                },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() }
            });
        }

        protected override void OnUpdate()
        {
            bool applied = LotBuildingMarkerSystem.ToolApplied;
            LotBuildingMarkerSystem.ToolApplied = false;
            bool alreadySent = PlaceCaptureSystem.SentThisFrame;
            PlaceCaptureSystem.SentThisFrame = false;
            SentThisFrame = false;
            if (!applied || alreadySent || Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null ||
                _createdBuildings.IsEmptyIgnoreFilter)
            {
                return;
            }

            using (NativeArray<Entity> buildings = _createdBuildings.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity building in buildings)
                {
                    var definitions = new List<DefinitionData>();
                    DefinitionData main = DescribeBuilding(building, out string ownerPrefab, out Game.Objects.Transform transform);
                    if (main == null)
                    {
                        continue;
                    }

                    definitions.Add(main);
                    AddAreas(building, ownerPrefab, transform, definitions);
                    AddSubNets(building, ownerPrefab, transform, definitions);
                    Command.SendToAll(new PlaceCommand { Definitions = definitions.ToArray() });
                    SentThisFrame = true;
                    BaseGameLog.Info($"Placement sent (lot building): {main.Prefab} with {definitions.Count - 1} part(s)");
                }
            }
        }

        private DefinitionData DescribeBuilding(Entity building, out string prefab, out Game.Objects.Transform transform)
        {
            transform = EntityManager.GetComponentData<Game.Objects.Transform>(building);
            prefab = PrefabLookup.Name(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(building).m_Prefab);
            if (prefab == null)
            {
                return null;
            }

            float elevation = EntityManager.HasComponent<Game.Objects.Elevation>(building)
                ? EntityManager.GetComponentData<Game.Objects.Elevation>(building).m_Elevation
                : 0f;
            int seed = EntityManager.HasComponent<PseudoRandomSeed>(building)
                ? EntityManager.GetComponentData<PseudoRandomSeed>(building).m_Seed
                : 0;
            return new DefinitionData
            {
                Prefab = prefab,
                SubPrefab = "",
                RandomSeed = seed,
                Original = new NetRef { Kind = 0 },
                Owner = new NetRef { Kind = 0 },
                Object = new SpawnCommand
                {
                    Position = NetRefs.F(transform.m_Position), LocalPosition = NetRefs.F(transform.m_Position),
                    Scale = NetRefs.F(float3.zero), Rotation = Definitions.Q(transform.m_Rotation),
                    LocalRotation = Definitions.Q(transform.m_Rotation), Elevation = elevation,
                    ParentMesh = -1, Probability = 100, PrefabSubIndex = -1
                }
            };
        }

        private DefinitionData Owned(string prefab, string ownerPrefab, Game.Objects.Transform owner)
        {
            return new DefinitionData
            {
                Prefab = prefab,
                SubPrefab = "",
                Original = new NetRef { Kind = 0 },
                Owner = new NetRef { Kind = 0 },
                HasOwnerDefinition = true,
                OwnerPrefab = ownerPrefab,
                OwnerPosition = NetRefs.F(owner.m_Position),
                OwnerRotation = Definitions.Q(owner.m_Rotation)
            };
        }

        private void AddAreas(Entity building, string ownerPrefab, Game.Objects.Transform transform, List<DefinitionData> definitions)
        {
            if (!EntityManager.HasBuffer<Game.Areas.SubArea>(building))
            {
                return;
            }

            DynamicBuffer<Game.Areas.SubArea> areas = EntityManager.GetBuffer<Game.Areas.SubArea>(building, true);
            var list = new List<Entity>();
            for (int i = 0; i < areas.Length; i++)
            {
                list.Add(areas[i].m_Area);
            }

            foreach (Entity area in list)
            {
                if (!EntityManager.Exists(area) || !EntityManager.HasBuffer<Game.Areas.Node>(area))
                {
                    continue;
                }

                string prefab = PrefabLookup.Name(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(area).m_Prefab);
                if (prefab == null)
                {
                    continue;
                }

                DynamicBuffer<Game.Areas.Node> nodes = EntityManager.GetBuffer<Game.Areas.Node>(area, true);
                var flat = new float[nodes.Length * 4];
                for (int i = 0; i < nodes.Length; i++)
                {
                    flat[i * 4] = nodes[i].m_Position.x;
                    flat[i * 4 + 1] = nodes[i].m_Position.y;
                    flat[i * 4 + 2] = nodes[i].m_Position.z;
                    flat[i * 4 + 3] = nodes[i].m_Elevation;
                }

                DefinitionData data = Owned(prefab, ownerPrefab, transform);
                data.AreaNodes = flat;
                definitions.Add(data);
            }
        }

        private void AddSubNets(Entity building, string ownerPrefab, Game.Objects.Transform transform, List<DefinitionData> definitions)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(building))
            {
                return;
            }

            DynamicBuffer<Game.Net.SubNet> nets = EntityManager.GetBuffer<Game.Net.SubNet>(building, true);
            var list = new List<Entity>();
            for (int i = 0; i < nets.Length; i++)
            {
                list.Add(nets[i].m_SubNet);
            }

            foreach (Entity net in list)
            {
                if (!EntityManager.Exists(net) || !EntityManager.HasComponent<Edge>(net) || !EntityManager.HasComponent<Curve>(net))
                {
                    continue;
                }

                string prefab = PrefabLookup.Name(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(net).m_Prefab);
                if (prefab == null)
                {
                    continue;
                }

                Edge edge = EntityManager.GetComponentData<Edge>(net);
                Bezier4x3 b = EntityManager.GetComponentData<Curve>(net).m_Bezier;
                float2 elevation = EntityManager.HasComponent<Game.Net.Elevation>(net)
                    ? EntityManager.GetComponentData<Game.Net.Elevation>(net).m_Elevation
                    : float2.zero;
                DefinitionData data = Owned(prefab, ownerPrefab, transform);
                data.Course = new NetCourseData
                {
                    Curve = new[] { b.a.x, b.a.y, b.a.z, b.b.x, b.b.y, b.b.z, b.c.x, b.c.y, b.c.z, b.d.x, b.d.y, b.d.z },
                    Elevation = new[] { elevation.x, elevation.y },
                    Length = MathUtils.Length(b),
                    FixedIndex = -1,
                    Start = End(edge.m_Start, MathUtils.StartTangent(b), 0f, CoursePosFlags.IsFirst),
                    End = End(edge.m_End, MathUtils.EndTangent(b), 1f, CoursePosFlags.IsLast)
                };
                definitions.Add(data);
            }
        }

        private CoursePosData End(Entity node, float3 tangent, float delta, CoursePosFlags flags)
        {
            float3 position = EntityManager.GetComponentData<Node>(node).m_Position;
            quaternion rotation = NetUtils.GetNodeRotation(tangent);
            // the node itself is new with the building, so it is matched by position on the other side
            return new CoursePosData
            {
                Entity = new NetRef { Kind = 0 },
                Position = NetRefs.F(position),
                Rotation = Definitions.Q(rotation),
                Elevation = new[] { 0f, 0f },
                CourseDelta = delta,
                Flags = (uint)flags,
                ParentMesh = -1
            };
        }
    }
}
