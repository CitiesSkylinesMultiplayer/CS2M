using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Applies zoning received from other players to the matching local block,
    ///     with the same rules as the game's ApplyZonesSystem.
    /// </summary>
    public partial class ZoneApplySystem : GameSystemBase
    {
        public static readonly InboundQueue<ZoneCellsCommand> Pending = new InboundQueue<ZoneCellsCommand>("ZoneApplySystem", 4096, 64);

        private const float IndexCellSize = 1f;

        private EntityQuery _blocks;

        protected override void OnCreate()
        {
            base.OnCreate();
            _blocks = GetEntityQuery(
                ComponentType.ReadOnly<Block>(),
                ComponentType.ReadOnly<Cell>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            // received while joining: wait until the city has loaded
            if (Pending.IsEmpty || Command.CurrentRole == MultiplayerRole.None)
            {
                return;
            }

            // blocks indexed by position once for this frame's commands; each lookup then checks the few blocks
            // within a metre instead of every block in the city
            Dictionary<long, List<int>> index = null;
            using (NativeArray<Entity> entities = _blocks.ToEntityArray(Allocator.Temp))
            using (NativeArray<Block> blocks = _blocks.ToComponentDataArray<Block>(Allocator.Temp))
            {
                while (Pending.TryDequeue(out ZoneCellsCommand command))
                {
                    try
                    {
                        index ??= Index(blocks);
                        Apply(FindBlock(index, entities, blocks, command), command);
                    }
                    catch (System.Exception e)
                    {
                        Inbound.Failed(nameof(ZoneApplySystem), e);
                    }
                }
            }
        }

        private void Apply(Entity target, ZoneCellsCommand command)
        {
            if (target == Entity.Null)
            {
                Inbound.Warn("zone-block-missing", $"Zoning received for a block that doesn't exist here ({command.PosX}, {command.PosY}, {command.PosZ})");
                return;
            }

            // the handler made Cells and Zones the same length
            DynamicBuffer<Cell> cells = EntityManager.GetBuffer<Cell>(target);
            int count = System.Math.Min(command.Cells.Length, command.Zones.Length);
            for (int i = 0; i < count; i++)
            {
                int index = command.Cells[i];
                if (index < 0 || index >= cells.Length)
                {
                    continue;
                }

                Cell cell = cells[index];
                var zone = new ZoneType { m_Index = command.Zones[i] };
                if ((cell.m_State & CellFlags.Overridden) != 0)
                {
                    if (cell.m_Zone.Equals(zone))
                    {
                        cell.m_State &= ~CellFlags.Overridden;
                    }
                }
                else
                {
                    cell.m_Zone = zone;
                }

                cells[index] = cell;
            }

            if (!EntityManager.HasComponent<Updated>(target))
            {
                EntityManager.AddComponent<Updated>(target);
            }

            BaseGameLog.Info($"Zoning received: {count} cells on block at ({command.PosX}, {command.PosY}, {command.PosZ})");
        }

        private static long Key(int x, int z) => ((long)x << 32) | (uint)z;

        private static Dictionary<long, List<int>> Index(NativeArray<Block> blocks)
        {
            var index = new Dictionary<long, List<int>>(blocks.Length);
            for (int i = 0; i < blocks.Length; i++)
            {
                float3 p = blocks[i].m_Position;
                long key = Key((int)math.floor(p.x / IndexCellSize), (int)math.floor(p.z / IndexCellSize));
                if (!index.TryGetValue(key, out List<int> list))
                {
                    index[key] = list = new List<int>(1);
                }

                list.Add(i);
            }

            return index;
        }

        private static Entity FindBlock(Dictionary<long, List<int>> index, NativeArray<Entity> entities, NativeArray<Block> blocks,
            ZoneCellsCommand command)
        {
            var position = new float3(command.PosX, command.PosY, command.PosZ);
            var direction = new float2(command.DirX, command.DirY);
            // the match distance (0.1 m) is less than a cell, so the neighbouring cells cover it
            int cx = (int)math.floor(position.x / IndexCellSize), cz = (int)math.floor(position.z / IndexCellSize);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!index.TryGetValue(Key(cx + dx, cz + dz), out List<int> list))
                    {
                        continue;
                    }

                    foreach (int i in list)
                    {
                        Block block = blocks[i];
                        if (block.m_Size.x == command.SizeX && block.m_Size.y == command.SizeY &&
                            math.distancesq(block.m_Position, position) < 0.01f &&
                            math.dot(block.m_Direction, direction) > 0.999f)
                        {
                            return entities[i];
                        }
                    }
                }
            }

            return Entity.Null;
        }
    }
}
