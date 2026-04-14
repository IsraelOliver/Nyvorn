using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Items
{
    public sealed class WorldItem
    {
        private readonly Texture2D texture;
        private Vector2 position;
        private float velocityX;
        private float velocityY;
        private float pickupDelayTimer;

        public WorldItem(
            ItemDefinition definition,
            Texture2D texture,
            Vector2 startPosition,
            float pickupDelay = 0f,
            float initialVelocityX = 0f,
            float initialVelocityY = 0f)
        {
            Definition = definition;
            this.texture = texture;
            position = startPosition;
            velocityX = initialVelocityX;
            velocityY = initialVelocityY;
            pickupDelayTimer = pickupDelay;
        }

        public ItemDefinition Definition { get; }
        public ItemId ItemId => Definition.Id;
        public Vector2 Position => position;
        public float VelocityX => velocityX;
        public float VelocityY => velocityY;
        public float PickupDelayRemaining => pickupDelayTimer > 0f ? pickupDelayTimer : 0f;
        public bool CanBePickedUp => pickupDelayTimer <= 0f;

        private float FrameLeft => position.X - Definition.WorldPivot.X;
        private float FrameTop => position.Y - Definition.WorldPivot.Y;
        private float Left => FrameLeft + Definition.WorldCollisionRect.X;
        private float Right => Left + Definition.WorldCollisionRect.Width - 1f;
        private float Top => FrameTop + Definition.WorldCollisionRect.Y;
        private float Bottom => Top + Definition.WorldCollisionRect.Height;

        public Rectangle WorldBounds => new Rectangle(
            (int)Left,
            (int)Top,
            Definition.WorldCollisionRect.Width,
            Definition.WorldCollisionRect.Height);

        public void Update(float dt, WorldMap worldMap)
        {
            float prevBottom = Bottom;
            float prevTop = Top;

            if (pickupDelayTimer > 0f)
                pickupDelayTimer -= dt;

            position.X += velocityX * dt;
            velocityX *= 0.88f;

            velocityY += PhysicsSettings.WorldGravity * Definition.GravityScale * dt;
            position.Y += velocityY * dt;

            ResolveWorldCollisionsY(worldMap, prevBottom, prevTop);
        }

        public void PullToward(Vector2 targetPosition, float dt, float pullStrength)
        {
            if (!CanBePickedUp)
                return;

            Vector2 offset = targetPosition - position;
            if (offset.LengthSquared() <= 0.0001f)
                return;

            offset.Normalize();
            velocityX += offset.X * pullStrength * dt;
            velocityY += offset.Y * pullStrength * dt;
        }

        public void ShiftX(float deltaX)
        {
            position.X += deltaX;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 topLeft = new Vector2(
                (float)System.Math.Round(FrameLeft),
                (float)System.Math.Round(FrameTop));

            spriteBatch.Draw(
                texture,
                topLeft,
                Definition.SourceRectangle,
                Color.White);
        }

        private void ResolveWorldCollisionsY(WorldMap worldMap, float prevBottom, float prevTop)
        {
            int ts = worldMap.TileSize;
            float left = Left + 1f;
            float right = Right - 1f;
            int tileXLeft = (int)System.MathF.Floor(left / ts);
            int tileXRight = (int)System.MathF.Floor(right / ts);

            if (velocityY > 0f)
            {
                int fromY = (int)(prevBottom / ts);
                int toY = (int)(Bottom / ts);

                for (int y = fromY; y <= toY; y++)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        float tileTop = y * ts;
                        position.Y = tileTop - Definition.WorldCollisionRect.Bottom + Definition.WorldPivot.Y;
                        velocityY = 0f;
                        return;
                    }
                }
            }
            else if (velocityY < 0f)
            {
                int fromY = (int)(prevTop / ts);
                int toY = (int)(Top / ts);

                for (int y = fromY; y >= toY; y--)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        float tileBottom = y * ts + ts;
                        position.Y = tileBottom - Definition.WorldCollisionRect.Y + Definition.WorldPivot.Y;
                        velocityY = 0f;
                        return;
                    }
                }
            }
        }
    }
}
