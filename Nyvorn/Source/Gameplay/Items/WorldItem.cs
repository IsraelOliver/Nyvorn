using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Items
{
    public sealed class WorldItem
    {
        private readonly Texture2D texture;
        private readonly KinematicBodyMotor kinematicMotor;
        private Vector2 position;
        private float velocityX;
        private float velocityY;
        private float pickupDelayTimer;
        // Set by PullToward each frame; while magnetized the item flies straight to the player,
        // ignoring gravity and tile collision (Terraria-style pickup pull through walls).
        private bool isMagnetized;

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
            kinematicMotor = new KinematicBodyMotor(startPosition);
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
            if (pickupDelayTimer > 0f)
                pickupDelayTimer -= dt;

            if (isMagnetized)
            {
                position.X += velocityX * dt;
                position.Y += velocityY * dt;
                kinematicMotor.Position = position;
                isMagnetized = false;
                return;
            }

            WorldCollisionQuery collision = WorldCollisionQuery.SolidTiles(worldMap);

            MoveHorizontally(collision, velocityX * dt);
            velocityX *= 0.88f;

            velocityY += PhysicsSettings.WorldGravity * Definition.GravityScale * dt;
            MoveVertically(collision, velocityY * dt);
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

            const float maxPullSpeed = 280f;
            Vector2 velocity = new(velocityX, velocityY);
            if (velocity.LengthSquared() > maxPullSpeed * maxPullSpeed)
            {
                velocity = Vector2.Normalize(velocity) * maxPullSpeed;
                velocityX = velocity.X;
                velocityY = velocity.Y;
            }

            isMagnetized = true;
        }

        public void ShiftX(float deltaX)
        {
            position.X += deltaX;
            kinematicMotor.Position = position;
        }

        public void Draw(SpriteBatch spriteBatch, Color tint)
        {
            Vector2 topLeft = new Vector2(
                (float)System.Math.Round(FrameLeft),
                (float)System.Math.Round(FrameTop));

            spriteBatch.Draw(
                texture,
                topLeft,
                Definition.SourceRectangle,
                tint);
        }

        private void MoveHorizontally(WorldCollisionQuery collision, float amount)
        {
            kinematicMotor.Position = position;

            kinematicMotor.MoveX(
                amount,
                (candidatePosition, axis, direction) => HasHorizontalSolidCollisionAt(collision, candidatePosition, direction),
                hit =>
                {
                    velocityX = 0f;
                    return false;
                });

            position = kinematicMotor.Position;
        }

        private bool HasHorizontalSolidCollisionAt(WorldCollisionQuery collision, Vector2 candidatePosition, int direction)
        {
            int ts = collision.TileSize;
            float top = GetTop(candidatePosition) + 1f;
            float bottom = GetBottom(candidatePosition) - 2f;
            int tileYTop = (int)System.MathF.Floor(top / ts);
            int tileYBottom = (int)System.MathF.Floor(bottom / ts);
            float edge = direction > 0
                ? GetRight(candidatePosition)
                : GetLeft(candidatePosition);
            int tileX = (int)System.MathF.Floor(edge / ts);

            return collision.IsBlockedAt(tileX, tileYTop) || collision.IsBlockedAt(tileX, tileYBottom);
        }

        private void MoveVertically(WorldCollisionQuery collision, float amount)
        {
            kinematicMotor.Position = position;

            kinematicMotor.MoveY(
                amount,
                (candidatePosition, axis, direction) => HasVerticalSolidCollisionAt(collision, candidatePosition, direction),
                hit =>
                {
                    velocityY = 0f;
                    return false;
                });

            position = kinematicMotor.Position;
        }

        private bool HasVerticalSolidCollisionAt(WorldCollisionQuery collision, Vector2 candidatePosition, int direction)
        {
            int ts = collision.TileSize;
            float left = GetLeft(candidatePosition) + 1f;
            float right = GetRight(candidatePosition) - 1f;
            int tileXLeft = (int)System.MathF.Floor(left / ts);
            int tileXRight = (int)System.MathF.Floor(right / ts);
            float edge = direction > 0
                ? GetBottom(candidatePosition) - 1f
                : GetTop(candidatePosition);
            int tileY = (int)System.MathF.Floor(edge / ts);

            return collision.IsBlockedAt(tileXLeft, tileY) || collision.IsBlockedAt(tileXRight, tileY);
        }

        private float GetFrameLeft(Vector2 candidatePosition) => candidatePosition.X - Definition.WorldPivot.X;
        private float GetFrameTop(Vector2 candidatePosition) => candidatePosition.Y - Definition.WorldPivot.Y;
        private float GetLeft(Vector2 candidatePosition) => GetFrameLeft(candidatePosition) + Definition.WorldCollisionRect.X;
        private float GetRight(Vector2 candidatePosition) => GetLeft(candidatePosition) + Definition.WorldCollisionRect.Width - 1f;
        private float GetTop(Vector2 candidatePosition) => GetFrameTop(candidatePosition) + Definition.WorldCollisionRect.Y;
        private float GetBottom(Vector2 candidatePosition) => GetTop(candidatePosition) + Definition.WorldCollisionRect.Height;
    }
}
