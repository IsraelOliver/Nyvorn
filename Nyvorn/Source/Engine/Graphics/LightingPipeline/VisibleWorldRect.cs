using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Authoritative visible world rectangle (zoom-adjusted).
    /// Shared source of truth between World Renderer and Lighting V3.
    /// All values in world-space pixels, float precision until tile conversion.
    /// </summary>
    public readonly struct VisibleWorldRect
    {
        public readonly float Left;
        public readonly float Top;
        public readonly float Right;
        public readonly float Bottom;

        public float Width => Right - Left;
        public float Height => Bottom - Top;

        public VisibleWorldRect(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        /// <summary>
        /// Create VisibleWorldRect from camera position and screen dimensions (with zoom applied).
        /// This is the authoritative calculation - used by both renderer and lighting.
        /// </summary>
        public static VisibleWorldRect FromCamera(
            float cameraX,
            float cameraY,
            int screenWidthPixels,
            int screenHeightPixels,
            float cameraZoom)
        {
            float viewWidth = screenWidthPixels / cameraZoom;
            float viewHeight = screenHeightPixels / cameraZoom;

            float left = cameraX - viewWidth / 2f;
            float right = left + viewWidth;
            float top = cameraY - viewHeight / 2f;
            float bottom = top + viewHeight;

            return new VisibleWorldRect(left, top, right, bottom);
        }

        public override string ToString()
        {
            return $"VisibleWorldRect(L:{Left:F1}, T:{Top:F1}, R:{Right:F1}, B:{Bottom:F1}, W:{Width:F1}x{Height:F1})";
        }
    }
}
