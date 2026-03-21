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
        private float velocityY;
        private float pickupDelayTimer;

        public WorldItem(ItemDefinition definition, Texture2D texture, Vector2 startPosition, float pickupDelay = 0f)
        {
            Definition = definition;
            this.texture = texture;
            position = startPosition;
            velocityY = 0f;
            pickupDelayTimer = pickupDelay;
        }

        public ItemDefinition Definition { get; }
        public ItemId ItemId => Definition.Id;
        public Vector2 Position => position;
        public bool CanBePickedUp => pickupDelayTimer <= 0f;

        private float Left => position.X - Definition.WorldPivot.X;
        private float Right => Left + Definition.WorldSize.X - 1f;
        private float Top => position.Y - Definition.WorldPivot.Y;
        private float Bottom => Top + Definition.WorldSize.Y;

        public Rectangle WorldBounds => new Rectangle((int)Left, (int)Top, Definition.WorldSize.X, Definition.WorldSize.Y);

        public void Update(float dt, WorldMap worldMap)
        {
            float prevBottom = Bottom;
            float prevTop = Top;

            if (pickupDelayTimer > 0f)
                pickupDelayTimer -= dt;

            velocityY += PhysicsSettings.WorldGravity * Definition.GravityScale * dt;
            position.Y += velocityY * dt;

            ResolveWorldCollisionsY(worldMap, prevBottom, prevTop);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 topLeft = new Vector2(
                (float)System.Math.Round(position.X - Definition.WorldPivot.X),
                (float)System.Math.Round(position.Y - Definition.WorldPivot.Y));

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
            int tileXLeft = (int)(left / ts);
            int tileXRight = (int)(right / ts);

            if (velocityY > 0f)
            {
                int fromY = (int)(prevBottom / ts);
                int toY = (int)(Bottom / ts);

                for (int y = fromY; y <= toY; y++)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        float tileTop = y * ts;
                        position.Y = tileTop - Definition.WorldSize.Y + Definition.WorldPivot.Y;
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
                        position.Y = tileBottom + Definition.WorldPivot.Y;
                        velocityY = 0f;
                        return;
                    }
                }
            }
        }
    }
}
