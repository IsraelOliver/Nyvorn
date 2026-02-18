using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.Engine.Graphics
{
    public class Camera2D
    {
        public Vector2 Position { get; private set; } = Vector2.Zero;
        public float Zoom { get; set; } = 2f;
        public float Rotation { get; set; } = 0f;

        public bool PixelPerfect { get; set; } = true;

        public float FollowLerp { get; set; } = 0f;

        public bool UseBounds { get; set; } = false;
        public Rectangle WorldBounds { get; set; } = Rectangle.Empty;

        public void Follow(Vector2 target, int screenW, int screenH)
        {
            float viewW = screenW / Zoom;
            float viewH = screenH / Zoom;

            Vector2 desired = target - new Vector2(viewW * 0.5f, viewH * 0.5f);

            if (UseBounds && WorldBounds != Rectangle.Empty)
            {
                float minX = WorldBounds.Left;
                float minY = WorldBounds.Top;
                float maxX = WorldBounds.Right - viewW;
                float maxY = WorldBounds.Bottom - viewH;

                desired.X = Math.Clamp(desired.X, minX, maxX);
                desired.Y = Math.Clamp(desired.Y, minY, maxY);
            }

            if (FollowLerp <= 0f)
                Position = desired;
            else
                Position = Vector2.Lerp(Position, desired, FollowLerp);
        }

        public Matrix GetViewMatrix()
        {
            Vector2 p = Position;

            if (PixelPerfect)
            {
                p.X = (float)Math.Round(p.X);
                p.Y = (float)Math.Round(p.Y);
            }

            return
                Matrix.CreateTranslation(new Vector3(-p, 0f)) *
                Matrix.CreateRotationZ(Rotation) *
                Matrix.CreateScale(Zoom, Zoom, 1f);
        }
    }
}