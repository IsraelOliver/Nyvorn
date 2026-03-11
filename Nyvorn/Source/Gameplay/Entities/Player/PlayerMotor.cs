using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class PlayerMotor
    {
        private const float JumpSpeed = 280f;
        private const float GravityScale = 1f;

        private Vector2 position;
        private Vector2 velocity;
        private float knockbackVelocityX;

        public PlayerMotor(Vector2 startPosition)
        {
            position = startPosition;
            velocity = Vector2.Zero;
            IsGrounded = false;
        }

        public Vector2 Position
        {
            get => position;
            set => position = value;
        }

        public Vector2 Velocity => velocity;
        public bool IsGrounded { get; private set; }

        private float HitLeft => position.X - (Player.HitW * 0.5f);
        private float HitRight => HitLeft + Player.HitW - 1f;
        private float HitBottom => position.Y;
        private float HitTop => HitBottom - Player.HitH + 1f;

        public Rectangle Hurtbox => new Rectangle((int)HitLeft, (int)HitTop, Player.HitW, Player.HitH);
        public float HitBottomValue => HitBottom;
        public float HitTopValue => HitTop;

        public void Update(float dt, WorldMap worldMap, float desiredVelocityX)
        {
            float prevHitBottom = HitBottom;
            float prevHitTop = HitTop;

            velocity.X = desiredVelocityX;
            float totalVelocityX = velocity.X + knockbackVelocityX;
            position.X += totalVelocityX * dt;
            ResolveWorldCollisionsX(worldMap, totalVelocityX);
            knockbackVelocityX = MathHelper.Lerp(knockbackVelocityX, 0f, MathHelper.Clamp(dt * 12f, 0f, 1f));

            ApplyGravity(dt);
            position.Y += velocity.Y * dt;
            ResolveWorldCollisionsY(worldMap, prevHitBottom, prevHitTop);
        }

        public void TryJump()
        {
            if (!IsGrounded)
                return;

            velocity.Y = -JumpSpeed;
            IsGrounded = false;
        }

        public void ApplyKnockback(float forceX, float forceY)
        {
            knockbackVelocityX = forceX;
            if (forceY < velocity.Y)
                velocity.Y = forceY;
        }

        private void ApplyGravity(float dt)
        {
            velocity.Y += PhysicsSettings.WorldGravity * GravityScale * dt;
        }

        private void ResolveWorldCollisionsX(WorldMap worldMap, float velocityX)
        {
            int ts = worldMap.TileSize;
            float top = HitTop + 1;
            float bottom = HitBottom - 1;
            int tileYTop = (int)(top / ts);
            int tileYBottom = (int)(bottom / ts);

            if (velocityX > 0)
            {
                float right = HitRight;
                int tileX = (int)(right / ts);

                if (worldMap.IsSolidAt(tileX, tileYTop) || worldMap.IsSolidAt(tileX, tileYBottom))
                {
                    float tileLeft = tileX * ts;
                    float newHitLeft = tileLeft - Player.HitW;
                    position.X = newHitLeft + (Player.HitW * 0.5f);
                    velocity.X = 0f;
                    knockbackVelocityX = 0f;
                }
            }
            else if (velocityX < 0)
            {
                float left = HitLeft;
                int tileX = (int)(left / ts);

                if (worldMap.IsSolidAt(tileX, tileYTop) || worldMap.IsSolidAt(tileX, tileYBottom))
                {
                    float tileRight = tileX * ts + ts;
                    float newHitLeft = tileRight;
                    position.X = newHitLeft + (Player.HitW * 0.5f);
                    velocity.X = 0f;
                    knockbackVelocityX = 0f;
                }
            }
        }

        private void ResolveWorldCollisionsY(WorldMap worldMap, float prevHitBottom, float prevHitTop)
        {
            IsGrounded = false;

            int ts = worldMap.TileSize;
            float left = HitLeft + 1;
            float right = HitRight - 1;
            int tileXLeft = (int)(left / ts);
            int tileXRight = (int)(right / ts);

            if (velocity.Y > 0)
            {
                int fromY = (int)(prevHitBottom / ts);
                int toY = (int)(HitBottom / ts);

                for (int y = fromY; y <= toY; y++)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        position.Y = y * ts;
                        velocity.Y = 0f;
                        IsGrounded = true;
                        return;
                    }
                }
            }
            else if (velocity.Y < 0)
            {
                int fromY = (int)(prevHitTop / ts);
                int toY = (int)(HitTop / ts);

                for (int y = fromY; y >= toY; y--)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        float tileBottom = y * ts + ts;
                        position.Y = tileBottom + Player.HitH - 1;
                        velocity.Y = 0f;
                        return;
                    }
                }
            }
        }
    }
}
