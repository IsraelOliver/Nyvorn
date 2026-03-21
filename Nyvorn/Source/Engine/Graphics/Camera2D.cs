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

        public float FollowLerpX { get; set; } = 0f;
        public float FollowLerpY { get; set; } = 0f;
        public float FollowSnapMarginX { get; set; } = 0f;
        public float FollowSnapMarginY { get; set; } = 0f;

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

            float nextX = FollowLerpX <= 0f ? desired.X : MathHelper.Lerp(Position.X, desired.X, FollowLerpX);
            float nextY = FollowLerpY <= 0f ? desired.Y : MathHelper.Lerp(Position.Y, desired.Y, FollowLerpY);
            Position = new Vector2(nextX, nextY);

            if (FollowSnapMarginX > 0f)
            {
                float minTargetX = Position.X + FollowSnapMarginX;
                float maxTargetX = Position.X + viewW - FollowSnapMarginX;

                if (target.X < minTargetX)
                    Position = new Vector2(target.X - FollowSnapMarginX, Position.Y);
                else if (target.X > maxTargetX)
                    Position = new Vector2(target.X - (viewW - FollowSnapMarginX), Position.Y);
            }

            if (FollowSnapMarginY > 0f)
            {
                float minTargetY = Position.Y + FollowSnapMarginY;
                float maxTargetY = Position.Y + viewH - FollowSnapMarginY;

                if (target.Y < minTargetY)
                    Position = new Vector2(Position.X, target.Y - FollowSnapMarginY);
                else if (target.Y > maxTargetY)
                    Position = new Vector2(Position.X, target.Y - (viewH - FollowSnapMarginY));
            }

            if (UseBounds && WorldBounds != Rectangle.Empty)
            {
                float minX = WorldBounds.Left;
                float minY = WorldBounds.Top;
                float maxX = WorldBounds.Right - viewW;
                float maxY = WorldBounds.Bottom - viewH;

                Position = new Vector2(
                    Math.Clamp(Position.X, minX, maxX),
                    Math.Clamp(Position.Y, minY, maxY));
            }
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

        public Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            Matrix inverseView = Matrix.Invert(GetViewMatrix());
            return Vector2.Transform(screenPosition, inverseView);
        }

        public Vector2 WorldToScreen(Vector2 worldPosition)
        {
            return Vector2.Transform(worldPosition, GetViewMatrix());
        }
    }
}
