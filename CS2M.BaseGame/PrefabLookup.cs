using System.Collections.Generic;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame
{
    /// <summary>
    ///     Prefab entities differ between games, so prefabs are sent by type and name.
    ///     (PrefabID also carries a content hash, which isn't needed when every player runs the same mods.)
    ///     <para>
    ///         The name map is built on the first lookup and rebuilt on a miss only when the number of prefabs
    ///         changed (prefabs can load after the first lookup), or when an unseen name misses and the map is
    ///         older than <see cref="StaleRebuildInterval" />; never more than once a second. Names that weren't
    ///         found are remembered until the next rebuild, so a missing asset, or a command full of made-up names,
    ///         costs a dictionary lookup instead of a walk over every prefab.
    ///     </para>
    /// </summary>
    internal static class PrefabLookup
    {
        private const float MinRebuildInterval = 1f;
        private const float StaleRebuildInterval = 30f;
        private const int MaxMissing = 4096;
        private const int MaxNames = 200000;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entity> ByName = new Dictionary<string, Entity>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static readonly Dictionary<Entity, string> Names = new Dictionary<Entity, string>();

        private static World _world;
        private static EntityQuery _prefabs;
        private static int _builtCount = -1;
        private static float _lastBuild = float.NegativeInfinity;

        public static string Name(PrefabSystem prefabSystem, Entity prefab)
        {
            lock (Gate)
            {
                if (Names.TryGetValue(prefab, out string cached))
                {
                    return cached;
                }
            }

            string name = prefabSystem.TryGetPrefab(prefab, out PrefabBase prefabBase)
                ? prefabBase.GetType().Name + "/" + prefabBase.name
                : null;
            if (name != null)
            {
                lock (Gate)
                {
                    if (Names.Count >= MaxNames)
                    {
                        Names.Clear();
                    }

                    Names[prefab] = name;
                }
            }

            return name;
        }

        public static Entity Find(EntityManager entityManager, PrefabSystem prefabSystem, string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > Inbound.MaxPrefabNameLength)
            {
                return Entity.Null;
            }

            lock (Gate)
            {
                if (ByName.TryGetValue(name, out Entity entity) && entityManager.Exists(entity))
                {
                    return entity;
                }

                if (RebuildDue(entityManager, Missing.Contains(name)))
                {
                    Rebuild(prefabSystem);
                    if (ByName.TryGetValue(name, out entity) && entityManager.Exists(entity))
                    {
                        return entity;
                    }
                }

                if (Missing.Count >= MaxMissing)
                {
                    Missing.Clear();
                }

                Missing.Add(name);
                return Entity.Null;
            }
        }

        private static bool RebuildDue(EntityManager entityManager, bool knownMiss)
        {
            if (_world != entityManager.World)
            {
                _world = entityManager.World;
                _prefabs = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>());
                _builtCount = -1;
            }

            if (_builtCount < 0)
            {
                return true;
            }

            float age = UnityEngine.Time.realtimeSinceStartup - _lastBuild;
            if (age < MinRebuildInterval)
            {
                return false;
            }

            return _prefabs.CalculateEntityCount() != _builtCount || (!knownMiss && age >= StaleRebuildInterval);
        }

        private static void Rebuild(PrefabSystem prefabSystem)
        {
            ByName.Clear();
            Missing.Clear();
            Names.Clear();
            using (NativeArray<Entity> prefabs = _prefabs.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity prefab in prefabs)
                {
                    if (prefabSystem.TryGetPrefab(prefab, out PrefabBase prefabBase))
                    {
                        string key = prefabBase.GetType().Name + "/" + prefabBase.name;
                        ByName[key] = prefab;
                        Names[prefab] = key;
                    }
                }

                _builtCount = prefabs.Length;
            }

            _lastBuild = UnityEngine.Time.realtimeSinceStartup;
        }
    }
}
