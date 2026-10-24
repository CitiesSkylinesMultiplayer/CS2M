using System.Collections.Generic;
using Colossal.Mathematics;
using CS2M.BaseGame.Commands;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Applies the tool definitions that point at something that already exists (CreationDefinition.m_Original).
    ///     The game only ever applies those through Temp entities (ApplyObjectsSystem copies the temp onto the
    ///     original); as permanent definitions GenerateObjectsSystem would build a second copy and hide the original.
    ///     So, the way ApplyObjectsSystem/ApplyNetSystem/ApplyAreasSystem treat them:
    ///     - Delete: the original is removed (the moved building's old roads and lots, trees cleared under a building).
    ///     - otherwise: the original takes the definition's transform (a move), prefab (an upgrade to another prefab)
    ///       and is marked Updated, which also lets FindOwnersSystem match it as the owner of new parts such as a
    ///       service extension or the moved building's rebuilt roads (their OwnerDefinition is that exact transform).
    /// </summary>
    internal static class ExistingParts
    {
        private const float AreaMatchDistanceSq = 0.25f;

        public static int Apply(EntityManager em, PrefabSystem prefabs, EntityCommandBuffer buffer, DefinitionData[] definitions)
        {
            // every original is found where it was before anything here moves
            var targets = new List<KeyValuePair<DefinitionData, Entity>>();
            var seen = new HashSet<Entity>();
            int missing = 0;
            foreach (DefinitionData data in definitions)
            {
                if (!data.HadOriginal)
                {
                    continue;
                }

                Entity original = Find(em, prefabs, data);
                if (original == Entity.Null)
                {
                    missing++;
                    continue;
                }

                // the tool can describe one original twice (GenerateObjectsSystem keeps one); the first wins
                if (seen.Add(original))
                {
                    targets.Add(new KeyValuePair<DefinitionData, Entity>(data, original));
                }
            }

            var deleted = new HashSet<Entity>();
            int changed = 0;
            foreach (KeyValuePair<DefinitionData, Entity> target in targets)
            {
                DefinitionData data = target.Key;
                Entity original = target.Value;
                if (((CreationFlags)data.CreationFlags & CreationFlags.Delete) != 0)
                {
                    em.AddComponent<Deleted>(original);
                    deleted.Add(original);
                }
                else
                {
                    if (data.Object != null && em.HasComponent<Transform>(original))
                    {
                        Update(em, prefabs, buffer, data, original);
                    }

                    MarkUpdated(em, original);
                }

                changed++;
            }

            FixRemovedRoads(em, deleted);
            if (missing > 0)
            {
                BaseGameLog.Warn($"Placement received: {missing} existing part(s) not found here");
            }

            return changed;
        }

        private static Entity Find(EntityManager em, PrefabSystem prefabs, DefinitionData data)
        {
            if (data.Original != null && data.Original.Kind != 0)
            {
                return NetRefs.Resolve(em, data.Original, out Entity entity, prefabs) ? entity : Entity.Null;
            }

            // areas (lots, parking areas) have no NetRef; the definition carries their current nodes
            return data.AreaNodes != null ? FindArea(em, data.AreaNodes) : Entity.Null;
        }

        private static Entity FindArea(EntityManager em, float[] flat)
        {
            int count = flat.Length / 4;
            if (count == 0)
            {
                return Entity.Null;
            }

            using (EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.ReadOnly<Game.Areas.Node>() },
                       None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
                   }))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    DynamicBuffer<Game.Areas.Node> nodes = em.GetBuffer<Game.Areas.Node>(entity, true);
                    if (nodes.Length != count)
                    {
                        continue;
                    }

                    bool same = true;
                    for (int i = 0; i < count && same; i++)
                    {
                        var position = new float3(flat[i * 4], flat[i * 4 + 1], flat[i * 4 + 2]);
                        same = math.distancesq(nodes[i].m_Position, position) < AreaMatchDistanceSq;
                    }

                    if (same)
                    {
                        return entity;
                    }
                }
            }

            return Entity.Null;
        }

        // What ApplyObjectsSystem copies from a temp object onto its original
        private static void Update(EntityManager em, PrefabSystem prefabs, EntityCommandBuffer buffer, DefinitionData data, Entity original)
        {
            SpawnCommand o = data.Object;
            Transform old = em.GetComponentData<Transform>(original);
            var transform = new Transform(NetRefs.V3(o.Position), Definitions.Q(o.Rotation));
            em.SetComponentData(original, transform);

            if (em.HasComponent<Game.Objects.Elevation>(original))
            {
                Game.Objects.Elevation elevation = em.GetComponentData<Game.Objects.Elevation>(original);
                elevation.m_Elevation = o.Elevation;
                em.SetComponentData(original, elevation);
            }

            // CreationFlags.Upgrade keeps the definition's prefab (GenerateObjectsSystem); anything else keeps the original's
            if (((CreationFlags)data.CreationFlags & CreationFlags.Upgrade) != 0 && !string.IsNullOrEmpty(data.Prefab))
            {
                Entity prefab = PrefabLookup.Find(em, prefabs, data.Prefab);
                PrefabRef prefabRef = em.GetComponentData<PrefabRef>(original);
                if (prefab != Entity.Null && prefab != prefabRef.m_Prefab)
                {
                    em.SetComponentData(original, new PrefabRef { m_Prefab = prefab });
                    BaseGameLog.Info($"Placement received: {PrefabLookup.Name(prefabs, prefabRef.m_Prefab)} upgraded to {data.Prefab}");
                }
            }

            if (((CreationFlags)data.CreationFlags & CreationFlags.Attach) != 0 && em.HasComponent<Attached>(original) &&
                data.Attached != null && NetRefs.Resolve(em, data.Attached, out Entity parent, prefabs) && parent != Entity.Null)
            {
                Attached attached = em.GetComponentData<Attached>(original);
                var moved = new Attached(parent, attached.m_Parent, 0f);
                if (em.HasComponent<Curve>(parent))
                {
                    MathUtils.Distance(em.GetComponentData<Curve>(parent).m_Bezier, transform.m_Position, out moved.m_CurvePosition);
                }

                em.SetComponentData(original, moved);
            }

            if (math.distancesq(old.m_Position, transform.m_Position) > 0.01f &&
                (em.HasComponent<Game.Buildings.Building>(original) || em.HasComponent<Game.Routes.TransportStop>(original)))
            {
                // citizens and vehicles heading for the building re-path (ApplyObjectsSystem sends the same event)
                Entity moved = buffer.CreateEntity();
                buffer.AddComponent<Event>(moved);
                buffer.AddComponent(moved, new Game.Pathfind.PathTargetMoved(original, old.m_Position, transform.m_Position));
                BaseGameLog.Info($"Placement received: {PrefabLookup.Name(prefabs, em.GetComponentData<PrefabRef>(original).m_Prefab)} moved");
            }
        }

        // Junctions left by removed segments: owned ones go with them when nothing else uses them, the rest rebuild
        private static void FixRemovedRoads(EntityManager em, HashSet<Entity> deleted)
        {
            var nodes = new HashSet<Entity>();
            foreach (Entity entity in deleted)
            {
                if (em.HasComponent<Edge>(entity))
                {
                    Edge edge = em.GetComponentData<Edge>(entity);
                    nodes.Add(edge.m_Start);
                    nodes.Add(edge.m_End);
                }
            }

            foreach (Entity node in nodes)
            {
                if (node == Entity.Null || !em.Exists(node) || deleted.Contains(node))
                {
                    continue;
                }

                bool unused = true;
                if (em.HasBuffer<ConnectedEdge>(node))
                {
                    foreach (ConnectedEdge connected in em.GetBuffer<ConnectedEdge>(node, true))
                    {
                        unused &= deleted.Contains(connected.m_Edge);
                    }
                }

                if (unused && em.HasComponent<Owner>(node))
                {
                    em.AddComponent<Deleted>(node);
                }
                else
                {
                    MarkUpdated(em, node);
                }
            }
        }

        private static void MarkUpdated(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<Updated>(entity))
            {
                em.AddComponent<Updated>(entity);
            }
        }
    }
}
