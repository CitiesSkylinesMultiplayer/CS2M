using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Host only. Runs just before GenerateObjectsSystem and sends each growable building definition
    ///     the zone spawner made (permanent + construction, no owner, not replacing anything).
    /// </summary>
    public partial class SpawnCaptureSystem : GameSystemBase
    {
        private EntityQuery _definitions;
        private PrefabSystem _prefabSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _definitions = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CreationDefinition>(), ComponentType.ReadOnly<ObjectDefinition>() },
                None = new[] { ComponentType.ReadOnly<OwnerDefinition>() }
            });
            RequireForUpdate(_definitions);
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole != MultiplayerRole.Server || Command.SendToAll == null)
            {
                return;
            }

            using (NativeArray<Entity> entities = _definitions.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    CreationDefinition definition = EntityManager.GetComponentData<CreationDefinition>(entity);
                    const CreationFlags spawn = CreationFlags.Permanent | CreationFlags.Construction;
                    if ((definition.m_Flags & spawn) != spawn || definition.m_Original != Entity.Null)
                    {
                        continue;
                    }

                    string prefab = PrefabLookup.Name(_prefabSystem, definition.m_Prefab);
                    if (prefab == null)
                    {
                        continue;
                    }

                    ObjectDefinition o = EntityManager.GetComponentData<ObjectDefinition>(entity);
                    Command.SendToAll(new SpawnCommand
                    {
                        Prefab = prefab,
                        CreationFlags = (uint)definition.m_Flags,
                        RandomSeed = definition.m_RandomSeed,
                        Attached = NetRefs.Describe(EntityManager, definition.m_Attached, _prefabSystem),
                        Position = NetRefs.F(o.m_Position),
                        LocalPosition = NetRefs.F(o.m_LocalPosition),
                        Scale = NetRefs.F(o.m_Scale),
                        Rotation = Definitions.Q(o.m_Rotation),
                        LocalRotation = Definitions.Q(o.m_LocalRotation),
                        Elevation = o.m_Elevation,
                        Intensity = o.m_Intensity,
                        Age = o.m_Age,
                        IsDecoration = o.m_IsDecoration,
                        ParentMesh = o.m_ParentMesh,
                        GroupIndex = o.m_GroupIndex,
                        Probability = o.m_Probability,
                        PrefabSubIndex = o.m_PrefabSubIndex
                    });
                    BaseGameLog.Info($"Spawn sent: {prefab} at {o.m_Position}");
                }
            }
        }
    }

    /// <summary>
    ///     Joiners only. Runs before ToolApplySystem and adds up the costs the same way it does,
    ///     so the host can charge the city for this player's action.
    /// </summary>
    public partial class ChargeCaptureSystem : GameSystemBase
    {
        private EntityQuery _temps;

        protected override void OnCreate()
        {
            base.OnCreate();
            _temps = GetEntityQuery(ComponentType.ReadOnly<Temp>());
            RequireForUpdate(_temps);
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole != MultiplayerRole.Client || Command.SendToServer == null)
            {
                return;
            }

            int total = 0;
            using (NativeArray<Temp> temps = _temps.ToComponentDataArray<Temp>(Allocator.Temp))
            {
                foreach (Temp temp in temps)
                {
                    if ((temp.m_Flags & TempFlags.Cancel) == 0)
                    {
                        total += temp.m_Cost;
                    }
                }
            }

            if (total != 0)
            {
                Command.SendToServer(new ChargeCommand { Amount = total });
            }
        }
    }
}
