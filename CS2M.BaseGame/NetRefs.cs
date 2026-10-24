using Colossal.Collections;
using Colossal.Mathematics;
using CS2M.BaseGame.Commands;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CS2M.BaseGame
{
    /// <summary>
    ///     Converts between road entities and NetRef (position-based) descriptions.
    /// </summary>
    internal static class NetRefs
    {
        // Positions match on every client up to float noise
        private const float MatchDistanceSq = 0.25f;

        public static float[] F(float3 v) => new[] { v.x, v.y, v.z };

        public static float3 V3(float[] a) => a != null && a.Length >= 3 ? new float3(a[0], a[1], a[2]) : float3.zero;

        public static NetRef Describe(EntityManager em, Entity entity, PrefabSystem prefabSystem = null)
        {
            if (entity == Entity.Null || !em.Exists(entity))
            {
                return new NetRef { Kind = 0 };
            }

            if (em.HasComponent<Node>(entity))
            {
                return new NetRef { Kind = 1, A = F(em.GetComponentData<Node>(entity).m_Position) };
            }

            if (em.HasComponent<Edge>(entity) && em.HasComponent<Curve>(entity))
            {
                Curve curve = em.GetComponentData<Curve>(entity);
                return new NetRef { Kind = 2, A = F(curve.m_Bezier.a), B = F(curve.m_Bezier.d) };
            }

            if (prefabSystem != null && em.HasComponent<Game.Objects.Transform>(entity) && em.HasComponent<PrefabRef>(entity))
            {
                return new NetRef
                {
                    Kind = 3,
                    A = F(em.GetComponentData<Game.Objects.Transform>(entity).m_Position),
                    Prefab = PrefabLookup.Name(prefabSystem, em.GetComponentData<PrefabRef>(entity).m_Prefab)
                };
            }

            return new NetRef { Kind = 0 };
        }

        /// <summary>
        ///     Finds the local entity for a NetRef. Returns false if it should exist but wasn't found.
        ///     Looks only near the position, in the game's own search trees (the ones its tools use), so a lookup
        ///     costs about the same in a small town and a huge city. Entities created this frame are in the trees
        ///     from the next frame on; callers that wait for something to appear retry anyway.
        /// </summary>
        public static bool Resolve(EntityManager em, NetRef netRef, out Entity entity, PrefabSystem prefabSystem = null)
        {
            entity = Entity.Null;
            if (netRef == null || netRef.Kind == 0)
            {
                return true;
            }

            if (netRef.Kind < 1 || netRef.Kind > 3 || !Inbound.InMap(netRef.A) || (netRef.Kind == 2 && !Inbound.InMap(netRef.B)))
            {
                return false;
            }

            float3 a = V3(netRef.A);
            if (netRef.Kind == 3)
            {
                Entity prefab = prefabSystem != null ? PrefabLookup.Find(em, prefabSystem, netRef.Prefab) : Entity.Null;
                if (prefab == Entity.Null)
                {
                    return false;
                }

                Game.Objects.SearchSystem objects = em.World?.GetExistingSystemManaged<Game.Objects.SearchSystem>();
                if (objects == null)
                {
                    return ScanObjects(em, prefab, a, out entity);
                }

                // static objects (buildings, props, trees, stops); vehicles and creatures are in the moving tree
                NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = objects.GetStaticSearchTree(true, out JobHandle staticDeps);
                staticDeps.Complete();
                if (NearestObject(em, tree, prefab, a, out entity))
                {
                    return true;
                }

                tree = objects.GetMovingSearchTree(true, out JobHandle movingDeps);
                movingDeps.Complete();
                return NearestObject(em, tree, prefab, a, out entity);
            }

            Game.Net.SearchSystem nets = em.World?.GetExistingSystemManaged<Game.Net.SearchSystem>();
            if (nets == null)
            {
                return netRef.Kind == 1 ? ScanNodes(em, a, out entity) : ScanEdges(em, a, V3(netRef.B), out entity);
            }

            NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree = nets.GetNetSearchTree(true, out JobHandle netDeps);
            netDeps.Complete();
            return netRef.Kind == 1 ? NearestNode(em, netTree, a, out entity) : NearestEdge(em, netTree, a, V3(netRef.B), out entity);
        }

        // ---------- search tree lookups ----------

        private struct Collector : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>, IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Found;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds))
                {
                    Found.Add(item);
                }
            }
        }

        private static NativeList<Entity> Near(NativeQuadTree<Entity, QuadTreeBoundsXZ> tree, float3 position)
        {
            float radius = math.sqrt(MatchDistanceSq);
            var collector = new Collector
            {
                Bounds = new Bounds2(position.xz - radius, position.xz + radius),
                Found = new NativeList<Entity>(16, Allocator.Temp)
            };
            tree.Iterate(ref collector);
            return collector.Found;
        }

        private static bool Live(EntityManager em, Entity entity) =>
            em.Exists(entity) && !em.HasComponent<Temp>(entity) && !em.HasComponent<Deleted>(entity);

        private static bool NearestObject(EntityManager em, NativeQuadTree<Entity, QuadTreeBoundsXZ> tree, Entity prefab, float3 a, out Entity entity)
        {
            entity = Entity.Null;
            float best = MatchDistanceSq;
            NativeList<Entity> found = Near(tree, a);
            try
            {
                for (int i = 0; i < found.Length; i++)
                {
                    Entity candidate = found[i];
                    if (!Live(em, candidate) || !em.HasComponent<PrefabRef>(candidate) ||
                        !em.HasComponent<Game.Objects.Transform>(candidate) ||
                        em.GetComponentData<PrefabRef>(candidate).m_Prefab != prefab)
                    {
                        continue;
                    }

                    float distance = math.distancesq(em.GetComponentData<Game.Objects.Transform>(candidate).m_Position, a);
                    if (distance < best)
                    {
                        best = distance;
                        entity = candidate;
                    }
                }
            }
            finally
            {
                found.Dispose();
            }

            return entity != Entity.Null;
        }

        private static bool NearestNode(EntityManager em, NativeQuadTree<Entity, QuadTreeBoundsXZ> tree, float3 a, out Entity entity)
        {
            entity = Entity.Null;
            float best = MatchDistanceSq;
            NativeList<Entity> found = Near(tree, a);
            try
            {
                for (int i = 0; i < found.Length; i++)
                {
                    Entity candidate = found[i];
                    if (!em.HasComponent<Node>(candidate) || !Live(em, candidate))
                    {
                        continue;
                    }

                    float distance = math.distancesq(em.GetComponentData<Node>(candidate).m_Position, a);
                    if (distance < best)
                    {
                        best = distance;
                        entity = candidate;
                    }
                }
            }
            finally
            {
                found.Dispose();
            }

            return entity != Entity.Null;
        }

        private static bool NearestEdge(EntityManager em, NativeQuadTree<Entity, QuadTreeBoundsXZ> tree, float3 a, float3 b, out Entity entity)
        {
            entity = Entity.Null;
            float best = float.MaxValue;
            NativeList<Entity> found = Near(tree, a);
            try
            {
                for (int i = 0; i < found.Length; i++)
                {
                    Entity candidate = found[i];
                    if (!em.HasComponent<Edge>(candidate) || !em.HasComponent<Curve>(candidate) || !Live(em, candidate))
                    {
                        continue;
                    }

                    Curve curve = em.GetComponentData<Curve>(candidate);
                    if (EdgeDistance(curve, a, b, out float distance) && distance < best)
                    {
                        best = distance;
                        entity = candidate;
                    }
                }
            }
            finally
            {
                found.Dispose();
            }

            return entity != Entity.Null;
        }

        private static bool EdgeDistance(Curve curve, float3 a, float3 b, out float distance)
        {
            float3 ca = curve.m_Bezier.a;
            float3 cd = curve.m_Bezier.d;
            float forward = math.max(math.distancesq(ca, a), math.distancesq(cd, b));
            float backward = math.max(math.distancesq(ca, b), math.distancesq(cd, a));
            distance = math.min(forward, backward);
            return distance < MatchDistanceSq;
        }

        // ---------- full scans, only if the search systems aren't there ----------

        private static bool ScanObjects(EntityManager em, Entity prefab, float3 a, out Entity entity)
        {
            entity = Entity.Null;
            using (EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                       None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
                   }))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            using (NativeArray<Game.Objects.Transform> transforms = query.ToComponentDataArray<Game.Objects.Transform>(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (prefabs[i].m_Prefab == prefab && math.distancesq(transforms[i].m_Position, a) < MatchDistanceSq)
                    {
                        entity = entities[i];
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ScanNodes(EntityManager em, float3 a, out Entity entity)
        {
            entity = Entity.Null;
            using (EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<Node>() },
                       None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
                   }))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            using (NativeArray<Node> nodes = query.ToComponentDataArray<Node>(Allocator.Temp))
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (math.distancesq(nodes[i].m_Position, a) < MatchDistanceSq)
                    {
                        entity = entities[i];
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ScanEdges(EntityManager em, float3 a, float3 b, out Entity entity)
        {
            entity = Entity.Null;
            using (EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
                       None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
                   }))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            using (NativeArray<Curve> curves = query.ToComponentDataArray<Curve>(Allocator.Temp))
            {
                for (int i = 0; i < curves.Length; i++)
                {
                    if (EdgeDistance(curves[i], a, b, out _))
                    {
                        entity = entities[i];
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
