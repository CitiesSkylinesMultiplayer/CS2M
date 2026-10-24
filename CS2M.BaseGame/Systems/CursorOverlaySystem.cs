using Game;
using Game.Rendering;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Draws a ring on the ground at each other player's cursor, in their colour.
    ///     Runs just before the game's OverlayRenderSystem, like the tools' own brush outlines.
    /// </summary>
    public partial class CursorOverlaySystem : GameSystemBase
    {
        private OverlayRenderSystem _overlayRenderSystem;
        private CameraUpdateSystem _cameraUpdateSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _overlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            _cameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
        }

        protected override void OnUpdate()
        {
            if (CursorSystem.Remote.Count == 0)
            {
                return;
            }

            float3 cameraPosition = _cameraUpdateSystem.TryGetViewer(out Viewer viewer) ? viewer.position : float3.zero;
            OverlayRenderSystem.Buffer buffer = _overlayRenderSystem.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            foreach (CursorSystem.RemoteCursor cursor in CursorSystem.Remote.Values)
            {
                if (!cursor.OnMap)
                {
                    continue;
                }

                // grow with distance so the ring stays visible when zoomed out
                float diameter = math.clamp(math.distance(cameraPosition, cursor.Position) * 0.03f, 6f, 250f);
                Color fill = cursor.Color;
                fill.a = 0.2f;
                buffer.DrawCircle(cursor.Color, fill, diameter * 0.12f, OverlayRenderSystem.StyleFlags.Projected,
                    new float2(0f, 1f), cursor.Position, diameter);
            }

            _overlayRenderSystem.AddBufferWriter(default);
        }
    }
}
