using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyMotor
    {
        private readonly EnemyConfig config;
        private readonly KinematicBodyMotor kinematicMotor;
        private Vector2 position;
        private float velocityY;
        private float knockbackVelocityX;
        private float horizontalVelocityX;

        public EnemyMotor(Vector2 startPosition, EnemyConfig config)
        {
            this.config = config;
            position = startPosition;
            kinematicMotor = new KinematicBodyMotor(startPosition);
            velocityY = 0f;
            knockbackVelocityX = 0f;
        }

        public Vector2 Position => position;
        public float KnockbackVelocityX => knockbackVelocityX;
        public float HorizontalVelocityX => horizontalVelocityX;

        private float HitLeft => position.X - (config.HurtboxSize.X * 0.5f);
        private float HitRight => HitLeft + config.HurtboxSize.X - 1f;
        private float HitBottom => position.Y;
        private float HitTop => HitBottom - config.HurtboxSize.Y + 1f;

        public Rectangle Hurtbox => new Rectangle((int)HitLeft, (int)HitTop, config.HurtboxSize.X, config.HurtboxSize.Y);

        public void Update(float dt, WorldMap worldMap, float desiredVelocityX)
        {
            WorldCollisionQuery collision = WorldCollisionQuery.SolidTiles(worldMap);

            float totalVelocityX = desiredVelocityX + knockbackVelocityX;
            horizontalVelocityX = totalVelocityX;
            MoveHorizontally(collision, totalVelocityX * dt);
            knockbackVelocityX = MathHelper.Lerp(knockbackVelocityX, 0f, MathHelper.Clamp(dt * config.KnockbackRecovery, 0f, 1f));

            velocityY += PhysicsSettings.WorldGravity * config.GravityScale * dt;
            MoveVertically(collision, velocityY * dt);
        }

        public void ApplyKnockback(float forceX, float forceY)
        {
            knockbackVelocityX = forceX;
            if (forceY < velocityY)
                velocityY = forceY;
        }

        public void ShiftX(float deltaX)
        {
            position.X += deltaX;
            kinematicMotor.Position = position;
        }

        private void MoveHorizontally(WorldCollisionQuery collision, float amount)
        {
            kinematicMotor.Position = position;

            kinematicMotor.MoveX(
                amount,
                (candidatePosition, axis, direction) => HasHorizontalSolidCollisionAt(collision, candidatePosition, direction),
                hit =>
                {
                    knockbackVelocityX = 0f;
                    horizontalVelocityX = 0f;
                    return false;
                });

            position = kinematicMotor.Position;
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

        private bool HasHorizontalSolidCollisionAt(WorldCollisionQuery collision, Vector2 candidatePosition, int direction)
        {
            int ts = collision.TileSize;
            float top = GetHitTop(candidatePosition) + 1f;
            float bottom = GetHitBottom(candidatePosition) - 1f;
            int tileYTop = (int)System.MathF.Floor(top / ts);
            int tileYBottom = (int)System.MathF.Floor(bottom / ts);
            float edge = direction > 0
                ? GetHitRight(candidatePosition)
                : GetHitLeft(candidatePosition);
            int tileX = (int)System.MathF.Floor(edge / ts);

            return collision.HasBlockedInColumn(tileX, tileYTop, tileYBottom);
        }

        private bool HasVerticalSolidCollisionAt(WorldCollisionQuery collision, Vector2 candidatePosition, int direction)
        {
            int ts = collision.TileSize;
            float left = GetHitLeft(candidatePosition) + 1f;
            float right = GetHitRight(candidatePosition) - 1f;
            int tileXLeft = (int)System.MathF.Floor(left / ts);
            int tileXRight = (int)System.MathF.Floor(right / ts);
            float edge = direction > 0
                ? GetHitBottom(candidatePosition) - 1f
                : GetHitTop(candidatePosition);
            int tileY = (int)System.MathF.Floor(edge / ts);

            return collision.IsBlockedAt(tileXLeft, tileY) || collision.IsBlockedAt(tileXRight, tileY);
        }

        private float GetHitLeft(Vector2 candidatePosition) => candidatePosition.X - (config.HurtboxSize.X * 0.5f);
        private float GetHitRight(Vector2 candidatePosition) => GetHitLeft(candidatePosition) + config.HurtboxSize.X - 1f;
        private static float GetHitBottom(Vector2 candidatePosition) => candidatePosition.Y;
        private float GetHitTop(Vector2 candidatePosition) => GetHitBottom(candidatePosition) - config.HurtboxSize.Y + 1f;
    }
}
