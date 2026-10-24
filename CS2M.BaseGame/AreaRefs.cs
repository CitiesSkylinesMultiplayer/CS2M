using CS2M.BaseGame.Commands;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using AreaNode = Game.Areas.Node;

namespace CS2M.BaseGame
{
    /// <summary>
    ///     Areas (districts, surfaces, extractor areas...) have no position of their own, so they are matched
    ///     between games by prefab and the exact node outline.
    /// </summary>
    internal static class AreaRefs
    {
        // matched on x/z only: node heights are recomputed from the terrain on each game
        private const float MatchDistanceSq = 0.25f;

        private static float Dist2(float3 a, float3 b) => math.distancesq(a.xz, b.xz);

        public static AreaKey Key(EntityManager em, PrefabSystem prefabSystem, Entity area)
        {
            string prefab = PrefabLookup.Name(prefabSystem, em.GetComponentData<PrefabRef>(area).m_Prefab);
            return prefab == null ? null : new AreaKey { Prefab = prefab, Nodes = Positions(em.GetBuffer<AreaNode>(area, true)) };
        }

        public static float[] Positions(DynamicBuffer<AreaNode> nodes)
        {
            var result = new float[nodes.Length * 3];
            for (int i = 0; i < nodes.Length; i++)
            {
                float3 p = nodes[i].m_Position;
                result[i * 3] = p.x;
                result[i * 3 + 1] = p.y;
                result[i * 3 + 2] = p.z;
            }

            return result;
        }

        public static bool Same(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Finds the local area with this prefab and outline, or Entity.Null.
        /// </summary>
        public static Entity Resolve(EntityManager em, PrefabSystem prefabSystem, AreaKey key)
        {
            if (key?.Nodes == null || key.Nodes.Length < 3)
            {
                return Entity.Null;
            }

            Entity prefab = PrefabLookup.Find(em, prefabSystem, key.Prefab);
            if (prefab == Entity.Null)
            {
                return Entity.Null;
            }

            int count = key.Nodes.Length / 3;
            var first = new float3(key.Nodes[0], key.Nodes[1], key.Nodes[2]);
            using (EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.ReadOnly<AreaNode>(), ComponentType.ReadOnly<PrefabRef>() },
                       None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
                   }))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (prefabs[i].m_Prefab != prefab)
                    {
                        continue;
                    }

                    DynamicBuffer<AreaNode> nodes = em.GetBuffer<AreaNode>(entities[i], true);
                    if (nodes.Length != count || Dist2(nodes[0].m_Position, first) >= MatchDistanceSq)
                    {
                        continue;
                    }

                    bool match = true;
                    for (int n = 1; n < count && match; n++)
                    {
                        var p = new float3(key.Nodes[n * 3], key.Nodes[n * 3 + 1], key.Nodes[n * 3 + 2]);
                        match = Dist2(nodes[n].m_Position, p) < MatchDistanceSq;
                    }

                    if (match)
                    {
                        return entities[i];
                    }
                }
            }

            return Entity.Null;
        }
    }
}
