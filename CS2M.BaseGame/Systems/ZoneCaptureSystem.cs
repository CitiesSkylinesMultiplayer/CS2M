using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Runs in the ApplyTool phase just before the game's ApplyZonesSystem, so only on the frame
    ///     a tool is applied. Reads the zone tool's temp blocks and sends the selected cells.
    /// </summary>
    public partial class ZoneCaptureSystem : GameSystemBase
    {
        // The apply phase runs on two frames for one click; the second, identical send is dropped
        private const float RepeatWindow = 0.5f;

        private readonly Dictionary<string, float> _recentlySent = new Dictionary<string, float>();
        private EntityQuery _tempBlocks;

        protected override void OnCreate()
        {
            base.OnCreate();
            _tempBlocks = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Block>(),
                ComponentType.ReadOnly<Cell>());
            RequireForUpdate(_tempBlocks);
        }

        protected override void OnUpdate()
        {
            if (Command.CurrentRole == MultiplayerRole.None || Command.SendToAll == null)
            {
                return;
            }

            using (NativeArray<Entity> entities = _tempBlocks.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    Temp temp = EntityManager.GetComponentData<Temp>(entity);
                    if ((temp.m_Flags & TempFlags.Delete) != 0 || temp.m_Original == Entity.Null ||
                        !EntityManager.HasBuffer<Cell>(temp.m_Original))
                    {
                        continue;
                    }

                    DynamicBuffer<Cell> cells = EntityManager.GetBuffer<Cell>(entity, true);
                    var indices = new List<int>();
                    var zones = new List<ushort>();
                    for (int i = 0; i < cells.Length; i++)
                    {
                        if ((cells[i].m_State & CellFlags.Selected) != 0)
                        {
                            indices.Add(i);
                            zones.Add(cells[i].m_Zone.m_Index);
                        }
                    }

                    if (indices.Count == 0)
                    {
                        continue;
                    }

                    Block block = EntityManager.GetComponentData<Block>(temp.m_Original);
                    string key = $"{block.m_Position}|{string.Join(",", indices)}|{string.Join(",", zones)}";
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (_recentlySent.TryGetValue(key, out float sentAt) && now - sentAt < RepeatWindow)
                    {
                        continue;
                    }

                    _recentlySent[key] = now;
                    if (_recentlySent.Count > 256)
                    {
                        _recentlySent.Clear();
                    }

                    Command.SendToAll(new ZoneCellsCommand
                    {
                        PosX = block.m_Position.x,
                        PosY = block.m_Position.y,
                        PosZ = block.m_Position.z,
                        DirX = block.m_Direction.x,
                        DirY = block.m_Direction.y,
                        SizeX = block.m_Size.x,
                        SizeY = block.m_Size.y,
                        Cells = indices.ToArray(),
                        Zones = zones.ToArray()
                    });
                    BaseGameLog.Info($"Zoning sent: {indices.Count} cells on block at {block.m_Position}");
                }
            }
        }
    }
}
