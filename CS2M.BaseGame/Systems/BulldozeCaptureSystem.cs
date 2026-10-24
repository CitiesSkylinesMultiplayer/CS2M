using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Runs in the ApplyTool phase before the game's apply systems. While the bulldozer is active,
    ///     every temp entity flagged Delete marks a real entity about to be removed; those are sent.
    /// </summary>
    public partial class BulldozeCaptureSystem : GameSystemBase
    {
        // the same removal seen again within this time is not sent twice
        private const float RepeatWindow = 0.5f;

        private EntityQuery _temps;
        private ToolSystem _toolSystem;
        private PrefabSystem _prefabSystem;

        private string _lastKey;
        private float _lastTime;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _temps = GetEntityQuery(ComponentType.ReadOnly<Temp>());
            RequireForUpdate(_temps);
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null ||
                !(_toolSystem.activeTool is BulldozeToolSystem))
            {
                return;
            }

            var targets = new List<NetRef>();
            using (NativeArray<Entity> entities = _temps.ToEntityArray(Allocator.Temp))
            using (NativeArray<Temp> temps = _temps.ToComponentDataArray<Temp>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Temp temp = temps[i];
                    if ((temp.m_Flags & TempFlags.Delete) == 0 || temp.m_Original == Entity.Null ||
                        !EntityManager.Exists(temp.m_Original))
                    {
                        continue;
                    }

                    NetRef target = NetRefs.Describe(EntityManager, temp.m_Original, _prefabSystem);
                    if (target.Kind != 0)
                    {
                        targets.Add(target);
                    }
                }
            }

            if (targets.Count == 0)
            {
                return;
            }

            string key = string.Join(";", targets.ConvertAll(t => t.Kind + string.Join(",", t.A)));
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (key == _lastKey && now - _lastTime < RepeatWindow)
            {
                return;
            }

            _lastKey = key;
            _lastTime = now;
            Command.SendToAll(new BulldozeCommand { Targets = targets.ToArray() });
            BaseGameLog.Info($"Bulldoze sent: {targets.Count} target(s)");
        }
    }

    /// <summary>
    ///     Removes what another player bulldozed, the same way the game's apply systems do (add Deleted).
    /// </summary>
    public partial class BulldozeApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<BulldozeCommand> Pending = new InboundQueue<BulldozeCommand>("BulldozeApplySystem", 1024, 8);

        private PrefabSystem _prefabSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        protected override void OnUpdate()
        {
            // received while joining: wait until the city has loaded
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            while (Pending.TryDequeue(out BulldozeCommand command))
            {
                try
                {
                    int removed = 0, missing = 0;
                    var deleted = new HashSet<Entity>();
                    foreach (NetRef target in command.Targets ?? new NetRef[0])
                    {
                        if (!NetRefs.Resolve(EntityManager, target, out Entity entity, _prefabSystem) || entity == Entity.Null)
                        {
                            // e.g. a growable that only spawned in the other game
                            missing++;
                            continue;
                        }

                        if (deleted.Add(entity))
                        {
                            EntityManager.AddComponent<Game.Common.Deleted>(entity);
                            removed++;
                        }
                    }

                    // junctions left behind by a removed road need their geometry rebuilt
                    foreach (Entity entity in deleted)
                    {
                        if (!EntityManager.HasComponent<Edge>(entity))
                        {
                            continue;
                        }

                        Edge edge = EntityManager.GetComponentData<Edge>(entity);
                        foreach (Entity node in new[] { edge.m_Start, edge.m_End })
                        {
                            if (node != Entity.Null && EntityManager.Exists(node) && !deleted.Contains(node) &&
                                !EntityManager.HasComponent<Game.Common.Updated>(node))
                            {
                                EntityManager.AddComponent<Game.Common.Updated>(node);
                            }
                        }
                    }

                    BaseGameLog.Info($"Bulldoze received: {removed} removed, {missing} not found here");
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(BulldozeApplySystem), e);
                }
            }
        }
    }
}
