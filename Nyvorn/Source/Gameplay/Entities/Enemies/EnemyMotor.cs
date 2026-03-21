using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyMotor
    {
        private readonly EnemyConfig config;
        private Vector2 position;
        private float velocityY;
        private float knockbackVelocityX;

        public EnemyMotor(Vector2 startPosition, EnemyConfig config)
        {
            this.config = config;
            position = startPosition;
            velocityY = 0f;
            knockbackVelocityX = 0f;
        }

        public Vector2 Position => position;
        public float KnockbackVelocityX => knockbackVelocityX;

        private float HitLeft => position.X - (config.HurtboxSize.X * 0.5f);
        private float HitRight => HitLeft + config.HurtboxSize.X - 1f;
        private float HitBottom => position.Y;
        private float HitTop => HitBottom - config.HurtboxSize.Y + 1f;

        public Rectangle Hurtbox => new Rectangle((int)HitLeft, (int)HitTop, config.HurtboxSize.X, config.HurtboxSize.Y);

        public void Update(float dt, WorldMap worldMap)
        {
            float prevHitBottom = HitBottom;
            float prevHitTop = HitTop;

            position.X += knockbackVelocityX * dt;
            knockbackVelocityX = MathHelper.Lerp(knockbackVelocityX, 0f, MathHelper.Clamp(dt * config.KnockbackRecovery, 0f, 1f));

            velocityY += PhysicsSettings.WorldGravity * config.GravityScale * dt;
            position.Y += velocityY * dt;
            ResolveWorldCollisionsY(worldMap, prevHitBottom, prevHitTop);
        }

        public void ApplyKnockback(float forceX, float forceY)
        {
            knockbackVelocityX = forceX;
            if (forceY < velocityY)
                velocityY = forceY;
        }

        private void ResolveWorldCollisionsY(WorldMap worldMap, float prevHitBottom, float prevHitTop)
        {
            int ts = worldMap.TileSize;

            float left = HitLeft + 1;
            float right = HitRight - 1;
            int tileXLeft = (int)(left / ts);
            int tileXRight = (int)(right / ts);

            if (velocityY > 0)
            {
                int fromY = (int)(prevHitBottom / ts);
                int toY = (int)(HitBottom / ts);

                for (int y = fromY; y <= toY; y++)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        position.Y = y * ts;
                        velocityY = 0f;
                        return;
                    }
                }
            }
            else if (velocityY < 0)
            {
                int fromY = (int)(prevHitTop / ts);
                int toY = (int)(HitTop / ts);

                for (int y = fromY; y >= toY; y--)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        float tileBottom = y * ts + ts;
                        position.Y = tileBottom + config.HurtboxSize.Y - 1f;
                        velocityY = 0f;
                        return;
                    }
                }
            }
        }
    }
}
