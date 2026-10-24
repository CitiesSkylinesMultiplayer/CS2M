using System.Collections.Generic;
using System.Text;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Policies;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using RouteColor = Game.Routes.Color;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Keeps line settings the same for everyone: colour, name, number and policies (active/inactive,
    ///     day/night, ticket price and vehicle count are all policies). The panels change these in several
    ///     places, so like taxes they are compared twice a second and a changed line is sent whole.
    /// </summary>
    public partial class TransitLineSettingsSystem : GameSystemBase
    {
        private const float CheckInterval = 0.5f;

        // a received change takes a couple of frames to land (policy events, names); ignore the line meanwhile
        private const float SuppressTime = 2f;

        // long enough for the maker's settings to arrive after the line itself
        private const float NewLineSuppressTime = 5f;

        private static readonly Dictionary<Entity, float> SuppressUntil = new Dictionary<Entity, float>();

        private EntityQuery _lines;
        private PrefabSystem _prefabSystem;
        private NameSystem _nameSystem;
        private Dictionary<Entity, string> _known;
        private float _lastCheck;

        public static void Suppress(Entity route)
        {
            SuppressUntil[route] = UnityEngine.Time.realtimeSinceStartup + SuppressTime;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            _lines = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<RouteWaypoint>(),
                    ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<RouteColor>()
                },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None)
            {
                _known = null;
                SuppressUntil.Clear();
                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastCheck < CheckInterval)
            {
                return;
            }

            _lastCheck = now;
            var current = new Dictionary<Entity, string>();
            using (NativeArray<Entity> lines = _lines.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity line in lines)
                {
                    TransitLineSettingsCommand settings = Describe(line);
                    string signature = Signature(settings);
                    current[line] = signature;
                    if (_known == null || Command.SendToAll == null)
                    {
                        continue;
                    }

                    bool send;
                    if (_known.TryGetValue(line, out string known))
                    {
                        send = known != signature &&
                               !(SuppressUntil.TryGetValue(line, out float until) && now < until);
                    }
                    else
                    {
                        // a new line: only its maker sends, so the line gets the maker's number and colour
                        send = TransitLineCaptureSystem.TakeLocallyCreated(Lines.Key(settings.Line));
                        if (!send)
                        {
                            // a line received from another player: the number and policies the game gives it
                            // here are provisional until the maker's settings arrive, so they aren't sent back
                            SuppressUntil[line] = now + NewLineSuppressTime;
                        }
                    }

                    if (send)
                    {
                        Command.SendToAll(settings);
                        BaseGameLog.Info($"Transit line settings sent: {settings.Line.Prefab} #{settings.Number}");
                    }
                }
            }

            _known = current;
            var expired = new List<Entity>();
            foreach (KeyValuePair<Entity, float> pair in SuppressUntil)
            {
                if (now >= pair.Value)
                {
                    expired.Add(pair.Key);
                }
            }

            foreach (Entity entity in expired)
            {
                SuppressUntil.Remove(entity);
            }
        }

        private TransitLineSettingsCommand Describe(Entity line)
        {
            _nameSystem.TryGetCustomName(line, out string name);
            var policies = new List<LinePolicyData>();
            if (EntityManager.HasBuffer<Policy>(line))
            {
                foreach (Policy policy in EntityManager.GetBuffer<Policy>(line, true))
                {
                    string policyName = PrefabLookup.Name(_prefabSystem, policy.m_Policy);
                    if (policyName != null)
                    {
                        policies.Add(new LinePolicyData
                        {
                            Policy = policyName,
                            Active = (policy.m_Flags & PolicyFlags.Active) != 0,
                            Adjustment = policy.m_Adjustment
                        });
                    }
                }
            }

            policies.Sort((a, b) => string.CompareOrdinal(a.Policy, b.Policy));
            return new TransitLineSettingsCommand
            {
                Line = Lines.Describe(EntityManager, _prefabSystem, line),
                Color = Lines.Pack(EntityManager.GetComponentData<RouteColor>(line).m_Color),
                Name = name ?? "",
                Number = EntityManager.HasComponent<RouteNumber>(line) ? EntityManager.GetComponentData<RouteNumber>(line).m_Number : 0,
                Policies = policies.ToArray()
            };
        }

        private static string Signature(TransitLineSettingsCommand settings)
        {
            var signature = new StringBuilder();
            signature.Append(settings.Color).Append('|').Append(settings.Name).Append('|').Append(settings.Number);
            foreach (LinePolicyData policy in settings.Policies)
            {
                signature.Append('|').Append(policy.Policy).Append(':').Append(policy.Active).Append(':').Append(policy.Adjustment.ToString("R"));
            }

            return signature.ToString();
        }
    }
}
