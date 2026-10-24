using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using LoanSystem = Game.Tools.LoanSystem;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     The host owns the city's progression. It sends XP, milestone, development points, credit limit and
    ///     loan every second, and its unlocked development tree nodes when they change. Joiners overwrite theirs
    ///     (their own XP gains are dropped by XPDrainSystem) and send loan changes and node purchases to the host
    ///     instead of keeping them.
    ///     MilestoneSystem stays on for joiners: with the host's XP it reaches the same milestones on its own
    ///     (rewards, unlocks, milestone popup). If it hasn't caught up after a few seconds the milestone is set here.
    /// </summary>
    public partial class ProgressionSyncSystem : GameSystemBase
    {
        public static readonly InboundQueue<CommandBase> Pending = new InboundQueue<CommandBase>("ProgressionSyncSystem", 512, 32);

        private const float SendInterval = 1f;
        private const float CheckInterval = 0.5f;
        private const float DevTreeRepeat = 10f;
        // a fresh role means a city that just loaded; let its own unlock pass settle before comparing
        private const float WarmUp = 3f;
        private const float CatchUpDelay = 5f;

        private CitySystem _citySystem;
        private LoanSystem _loanSystem;
        private DevTreeSystem _devTreeSystem;
        private PrefabSystem _prefabSystem;
        private EntityQuery _milestoneLevel;
        private EntityQuery _milestones;
        private EntityQuery _devTreeNodes;
        private EntityArchetype _unlockEventArchetype;
        private EntityArchetype _milestoneEventArchetype;

        private MultiplayerRole _role = MultiplayerRole.None;
        private float _roleSince;
        private float _lastSend;
        private float _lastTreeSend;
        private float _lastCheck;
        private HashSet<string> _sentNodes;
        private HashSet<string> _knownNodes;
        private int? _knownLoan;
        private int _hostMilestone = -1;
        private float _behindSince = -1f;

        protected override void OnCreate()
        {
            base.OnCreate();
            _citySystem = World.GetOrCreateSystemManaged<CitySystem>();
            _loanSystem = World.GetOrCreateSystemManaged<LoanSystem>();
            _devTreeSystem = World.GetOrCreateSystemManaged<DevTreeSystem>();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _milestoneLevel = GetEntityQuery(ComponentType.ReadWrite<MilestoneLevel>());
            _milestones = GetEntityQuery(ComponentType.ReadOnly<MilestoneData>());
            _devTreeNodes = GetEntityQuery(ComponentType.ReadOnly<DevTreeNodeData>());
            // same events MilestoneSystem and DevTreeSystem create
            _unlockEventArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Event>(), ComponentType.ReadWrite<Unlock>());
            _milestoneEventArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Event>(), ComponentType.ReadWrite<MilestoneReachedEvent>());
        }

        protected override void OnUpdate()
        {
            MultiplayerRole role = Command.CurrentRole;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (role != _role)
            {
                _role = role;
                _roleSince = now;
                _sentNodes = null;
                _knownNodes = null;
                _knownLoan = null;
                _hostMilestone = -1;
                _behindSince = -1f;
            }

            if (role == MultiplayerRole.None || now - _roleSince < WarmUp || _citySystem.City == Entity.Null)
            {
                return;
            }

            bool check = now - _lastCheck >= CheckInterval;
            if (check)
            {
                _lastCheck = now;
            }

            if (role == MultiplayerRole.Client && check)
            {
                // local changes first, so a host update applied below isn't mistaken for one
                SendLocalLoan();
                SendLocalDevTree();
            }

            while (Pending.TryDequeue(out CommandBase command))
            {
                try
                {
                    // the handlers already refuse these from the wrong side; the role checks are a second layer
                    switch (command)
                    {
                        case ProgressionCommand progression when role == MultiplayerRole.Client:
                            ApplyProgression(progression);
                            break;
                        case DevTreeCommand tree when role == MultiplayerRole.Client:
                            ApplyDevTree(tree.Nodes);
                            break;
                        case LoanRequestCommand loan when role == MultiplayerRole.Server:
                            // the game's own path: clamps to the credit limit and pays out or takes back the money
                            // (LoanSystem applies it later this frame; the next regular send carries the result)
                            _loanSystem.ChangeLoan(loan.Amount);
                            BaseGameLog.Info($"Loan request received: {loan.Amount}");
                            break;
                        case DevTreeRequestCommand node when role == MultiplayerRole.Server:
                            PurchaseNode(node.Node);
                            break;
                    }
                }
                catch (System.Exception e)
                {
                    Inbound.Failed(nameof(ProgressionSyncSystem), e);
                }
            }

            if (role == MultiplayerRole.Server)
            {
                SendProgression(now);
                if (check)
                {
                    SendDevTree(now);
                }
            }
            else
            {
                CatchUpMilestone(now);
            }
        }

        // ---------- host ----------

        private void SendProgression(float now)
        {
            if (now - _lastSend < SendInterval)
            {
                return;
            }

            _lastSend = now;
            Entity city = _citySystem.City;
            var command = new ProgressionCommand { Milestone = GetMilestone(), DevTreePoints = _devTreeSystem.points };
            if (EntityManager.HasComponent<XP>(city))
            {
                XP xp = EntityManager.GetComponentData<XP>(city);
                command.XP = xp.m_XP;
                command.MaximumPopulation = xp.m_MaximumPopulation;
                command.MaximumIncome = xp.m_MaximumIncome;
                command.XPRewardRecord = (int)xp.m_XPRewardRecord;
            }

            if (EntityManager.HasComponent<Creditworthiness>(city))
            {
                command.Creditworthiness = EntityManager.GetComponentData<Creditworthiness>(city).m_Amount;
            }

            if (EntityManager.HasComponent<Loan>(city))
            {
                Loan loan = EntityManager.GetComponentData<Loan>(city);
                command.LoanAmount = loan.m_Amount;
                command.LoanLastModified = loan.m_LastModified;
            }

            Command.SendToAll?.Invoke(command);
        }

        private void SendDevTree(float now)
        {
            HashSet<string> nodes = UnlockedNodes();
            bool changed = _sentNodes == null || !nodes.SetEquals(_sentNodes);
            if (!changed && now - _lastTreeSend < DevTreeRepeat)
            {
                return;
            }

            _lastTreeSend = now;
            _sentNodes = nodes;
            Command.SendToAll?.Invoke(new DevTreeCommand { Nodes = new List<string>(nodes).ToArray() });
        }

        private void PurchaseNode(string name)
        {
            Entity node = PrefabLookup.Find(EntityManager, _prefabSystem, name);
            if (node == Entity.Null || !EntityManager.HasComponent<DevTreeNodeData>(node))
            {
                BaseGameLog.Warn($"Development node request for an unknown node {name}");
                return;
            }

            // Purchase checks points, requirements and that it's still locked, the same as a click here
            int before = _devTreeSystem.points;
            _devTreeSystem.Purchase(node);
            BaseGameLog.Info(_devTreeSystem.points != before
                ? $"Development node request received: {name}"
                : $"Development node request refused (points or requirements): {name}");
            _lastSend = 0f;
        }

        // ---------- joiners ----------

        private void ApplyProgression(ProgressionCommand command)
        {
            // a loan taken here since the last check would be overwritten below; send it first
            SendLocalLoan();
            Entity city = _citySystem.City;
            if (EntityManager.HasComponent<XP>(city))
            {
                XP xp = EntityManager.GetComponentData<XP>(city);
                xp.m_XP = command.XP;
                xp.m_MaximumPopulation = command.MaximumPopulation;
                xp.m_MaximumIncome = command.MaximumIncome;
                xp.m_XPRewardRecord = (XPRewardFlags)command.XPRewardRecord;
                EntityManager.SetComponentData(city, xp);
            }

            // When the host is a milestone ahead, its points and credit limit already include that milestone's
            // reward, and this game's MilestoneSystem is about to add it again. Keep ours until we've caught up,
            // or a purchase made meanwhile could spend points the host doesn't have.
            int local = GetMilestone();
            bool catchingUp = local >= 0 && command.Milestone > local;
            if (!catchingUp && EntityManager.HasComponent<Creditworthiness>(city))
            {
                EntityManager.SetComponentData(city, new Creditworthiness { m_Amount = command.Creditworthiness });
            }

            if (EntityManager.HasComponent<Loan>(city))
            {
                EntityManager.SetComponentData(city, new Loan
                {
                    m_Amount = command.LoanAmount, m_LastModified = command.LoanLastModified
                });
                _knownLoan = command.LoanAmount;
            }

            if (!catchingUp)
            {
                _devTreeSystem.points = command.DevTreePoints;
            }

            // never further than the last milestone this game has
            _hostMilestone = System.Math.Min(command.Milestone, LastMilestone());
        }

        private int LastMilestone()
        {
            int last = 0;
            using (NativeArray<MilestoneData> data = _milestones.ToComponentDataArray<MilestoneData>(Allocator.Temp))
            {
                foreach (MilestoneData milestone in data)
                {
                    last = System.Math.Max(last, milestone.m_Index);
                }
            }

            return last;
        }

        private void SendLocalLoan()
        {
            Entity city = _citySystem.City;
            if (!EntityManager.HasComponent<Loan>(city))
            {
                return;
            }

            int loan = EntityManager.GetComponentData<Loan>(city).m_Amount;
            if (_knownLoan.HasValue && loan != _knownLoan.Value)
            {
                Command.SendToServer?.Invoke(new LoanRequestCommand { Amount = loan });
                BaseGameLog.Info($"Loan change sent: {_knownLoan.Value} -> {loan}");
            }

            _knownLoan = loan;
        }

        private void SendLocalDevTree()
        {
            HashSet<string> nodes = UnlockedNodes();
            if (_knownNodes != null)
            {
                foreach (string node in nodes)
                {
                    if (!_knownNodes.Contains(node))
                    {
                        Command.SendToServer?.Invoke(new DevTreeRequestCommand { Node = node });
                        BaseGameLog.Info($"Development node purchase sent: {node}");
                    }
                }
            }

            _knownNodes = nodes;
        }

        private void ApplyDevTree(string[] names)
        {
            if (names == null)
            {
                return;
            }

            HashSet<string> local = UnlockedNodes();
            var buffer = new EntityCommandBuffer(Allocator.Temp);
            foreach (string name in names)
            {
                if (local.Contains(name))
                {
                    continue;
                }

                Entity node = PrefabLookup.Find(EntityManager, _prefabSystem, name);
                if (node == Entity.Null || !EntityManager.HasComponent<DevTreeNodeData>(node))
                {
                    continue;
                }

                Entity unlock = buffer.CreateEntity(_unlockEventArchetype);
                buffer.SetComponent(unlock, new Unlock(node));
                // known now, so the unlock isn't sent back to the host as a purchase
                _knownNodes?.Add(name);
                BaseGameLog.Info($"Development node received: {name}");
            }

            buffer.Playback(EntityManager);
            buffer.Dispose();
        }

        private void CatchUpMilestone(float now)
        {
            int local = GetMilestone();
            if (_hostMilestone < 0 || local < 0 || _hostMilestone <= local)
            {
                _behindSince = -1f;
                return;
            }

            if (_behindSince < 0f)
            {
                _behindSince = now;
                return;
            }

            if (now - _behindSince < CatchUpDelay)
            {
                return;
            }

            // MilestoneSystem didn't follow the host's XP (it waits while the tutorial intro is open):
            // raise the events it would have; money, credit and points come from the host anyway
            _behindSince = -1f;
            var buffer = new EntityCommandBuffer(Allocator.Temp);
            using (NativeArray<Entity> entities = _milestones.ToEntityArray(Allocator.Temp))
            using (NativeArray<MilestoneData> data = _milestones.ToComponentDataArray<MilestoneData>(Allocator.Temp))
            {
                for (int index = local + 1; index <= _hostMilestone; index++)
                {
                    Entity milestone = Entity.Null;
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i].m_Index == index)
                        {
                            milestone = entities[i];
                            break;
                        }
                    }

                    Entity reached = buffer.CreateEntity(_milestoneEventArchetype);
                    buffer.SetComponent(reached, new MilestoneReachedEvent(milestone, index));
                    if (milestone != Entity.Null)
                    {
                        Entity unlock = buffer.CreateEntity(_unlockEventArchetype);
                        buffer.SetComponent(unlock, new Unlock(milestone));
                    }
                }
            }

            buffer.Playback(EntityManager);
            buffer.Dispose();
            _milestoneLevel.SetSingleton(new MilestoneLevel { m_AchievedMilestone = _hostMilestone });
            BaseGameLog.Info($"Milestone set to the host's: {local} -> {_hostMilestone}");
        }

        // ---------- shared ----------

        private int GetMilestone() =>
            _milestoneLevel.CalculateEntityCount() == 1
                ? _milestoneLevel.GetSingleton<MilestoneLevel>().m_AchievedMilestone
                : -1;

        private HashSet<string> UnlockedNodes()
        {
            var nodes = new HashSet<string>();
            using (NativeArray<Entity> entities = _devTreeNodes.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (EntityManager.HasComponent<Locked>(entity) && EntityManager.IsComponentEnabled<Locked>(entity))
                    {
                        continue;
                    }

                    string name = PrefabLookup.Name(_prefabSystem, entity);
                    if (name != null)
                    {
                        nodes.Add(name);
                    }
                }
            }

            return nodes;
        }
    }

    /// <summary>
    ///     Joiners only. Runs just before XPSystem and throws away the XP this game's simulation queued,
    ///     so XP only comes from the host (ProgressionSyncSystem).
    /// </summary>
    public partial class XPDrainSystem : GameSystemBase
    {
        private XPSystem _xpSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _xpSystem = World.GetOrCreateSystemManaged<XPSystem>();
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole != MultiplayerRole.Client)
            {
                return;
            }

            var queue = _xpSystem.GetQueue(out Unity.Jobs.JobHandle writers);
            writers.Complete();
            queue.Clear();
        }
    }
}
