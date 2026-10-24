using CS2M.BaseGame.Commands;
using Game;
using Game.Tools;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Converts the road tool's course ends for the wire (<see cref="Pos" />, also used by the placement sync).
    ///     Roads themselves are sent as the applied result by NetDiffCaptureSystem: permanent definitions can't
    ///     split an existing road (only the preview path does), so replaying the tool's definitions on the other
    ///     games breaks junctions. The system is therefore disabled and sends nothing.
    /// </summary>
    public partial class RoadCaptureSystem : GameSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
        }

        protected override void OnUpdate()
        {
        }

        public static CoursePosData Pos(EntityManager em, CoursePos pos)
        {
            return new CoursePosData
            {
                Entity = NetRefs.Describe(em, pos.m_Entity),
                Position = NetRefs.F(pos.m_Position),
                Rotation = new[] { pos.m_Rotation.value.x, pos.m_Rotation.value.y, pos.m_Rotation.value.z, pos.m_Rotation.value.w },
                Elevation = new[] { pos.m_Elevation.x, pos.m_Elevation.y },
                CourseDelta = pos.m_CourseDelta,
                SplitPosition = pos.m_SplitPosition,
                Flags = (uint)pos.m_Flags,
                ParentMesh = pos.m_ParentMesh
            };
        }
    }
}
