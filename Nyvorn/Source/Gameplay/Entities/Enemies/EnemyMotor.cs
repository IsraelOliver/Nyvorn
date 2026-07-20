using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyMotor
    {
        // Mirrors PlayerMotor's sand-support tuning (PlayerMotor.MinSandSupportWidth /
        // SandSurfaceHeightTolerance) so enemies settle onto loose sand the same way the player does.
        private const int MinSandSupportWidth = 4;
        private const int SandSurfaceHeightTolerance = 1;

        private readonly EnemyConfig config;
        private readonly KinematicBodyMotor kinematicMotor;
        private Vector2 position;
        private float velocityY;
        private float knockbackVelocityX;
        private float horizontalVelocityX;
        private float pendingVerticalLandingY;

        public EnemyMotor(Vector2 startPosition, EnemyConfig config)
        {
            this.config = config;
            position = startPosition;
            kinematicMotor = new KinematicBodyMotor(startPosition);
            velocityY = 0f;
            knockbackVelocityX = 0f;
        }

        public Vector2 Position => position;
        public float HorizontalVelocityX => horizontalVelocityX;
        public float VelocityY => velocityY;
        public bool OnGround { get; private set; }
        public bool BlockedHorizontally { get; private set; }

        private float HitLeft => position.X - (config.HurtboxSize.X * 0.5f);
        private float HitRight => HitLeft + config.HurtboxSize.X - 1f;
        private float HitBottom => position.Y;
        private float HitTop => HitBottom - config.HurtboxSize.Y + 1f;

        public Rectangle Hurtbox => new Rectangle((int)HitLeft, (int)HitTop, config.HurtboxSize.X, config.HurtboxSize.Y);

        public void Update(float dt, WorldMap worldMap, SandSystem sandSystem, float desiredVelocityX, bool wantsJump = false)
        {
            WorldCollisionQuery collision = WorldCollisionQuery.SolidTiles(worldMap);

            float totalVelocityX = desiredVelocityX + knockbackVelocityX;
            horizontalVelocityX = totalVelocityX;
            MoveHorizontally(collision, totalVelocityX * dt);
            knockbackVelocityX = MathHelper.Lerp(knockbackVelocityX, 0f, MathHelper.Clamp(dt * config.KnockbackRecovery, 0f, 1f));

            if (wantsJump && OnGround)
            {
                velocityY = -config.JumpSpeed;
                OnGround = false;
            }

            velocityY += PhysicsSettings.WorldGravity * config.GravityScale * dt;
            MoveVertically(collision, sandSystem, velocityY * dt);
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
            BlockedHorizontally = false;
            bool usedStepUp = false;

            kinematicMotor.MoveX(
                amount,
                (candidatePosition, axis, direction) => HasHorizontalSolidCollisionAt(collision, candidatePosition, direction),
                hit =>
                {
                    position = kinematicMotor.Position;
                    if (!usedStepUp && TryStepUp(collision, hit.Direction))
                    {
                        usedStepUp = true;
                        kinematicMotor.Position = position;
                        return true;
                    }

                    knockbackVelocityX = 0f;
                    horizontalVelocityX = 0f;
                    BlockedHorizontally = true;
                    return false;
                });

            position = kinematicMotor.Position;
        }

        // Same single-tile step-up the player has (PlayerMotor.TryStepUp): a 1-tile-high bump
        // directly ahead with clear headroom above it is walked straight over instead of stopping
        // (and instead of the Fighter-AI wall-bump jump reflex kicking in for something this small).
        private bool TryStepUp(WorldCollisionQuery collision, int moveDir)
        {
            if (!OnGround)
                return false;

            int ts = collision.TileSize;
            float frontX = moveDir > 0 ? HitRight + 1f : HitLeft - 1f;
            int tileX = (int)System.MathF.Floor(frontX / ts);
            int tileYBottom = (int)System.MathF.Floor((HitBottom - 1f) / ts);
            int tileYAbove = tileYBottom - 1;

            if (!collision.IsBlockedAt(tileX, tileYBottom) || collision.IsBlockedAt(tileX, tileYAbove))
                return false;

            float originalY = position.Y;
            position.Y -= ts;

            if (HasBlockedOverlap(collision))
            {
                position.Y = originalY;
                return false;
            }

            return true;
        }

        private bool HasBlockedOverlap(WorldCollisionQuery collision)
        {
            int ts = collision.TileSize;
            int tileXLeft = (int)System.MathF.Floor((HitLeft + 1f) / ts);
            int tileXRight = (int)System.MathF.Floor((HitRight - 1f) / ts);
            int tileYTop = (int)System.MathF.Floor(HitTop / ts);
            int tileYBottom = (int)System.MathF.Floor((HitBottom - 1f) / ts);

            return collision.HasBlockedInArea(tileXLeft, tileXRight, tileYTop, tileYBottom);
        }

        private void MoveVertically(WorldCollisionQuery collision, SandSystem sandSystem, float amount)
        {
            OnGround = false;
            pendingVerticalLandingY = 0f;
            kinematicMotor.Position = position;

            kinematicMotor.MoveY(
                amount,
                (candidatePosition, axis, direction) => HasVerticalWorldCollisionAt(collision, sandSystem, candidatePosition, direction),
                hit =>
                {
                    kinematicMotor.Position = new Vector2(kinematicMotor.Position.X, pendingVerticalLandingY);
                    position = kinematicMotor.Position;
                    velocityY = 0f;
                    OnGround = hit.Direction > 0;
                    return false;
                });

            position = kinematicMotor.Position;

            if (!OnGround && velocityY >= 0f && HasGroundSupportAtCurrentPosition(collision, sandSystem))
            {
                velocityY = 0f;
                OnGround = true;
            }
        }

        // Combines tile collision with loose-sand support (PlayerMotor.HasVerticalWorldCollisionAt):
        // whichever surface the falling body reaches first - the tile grid below or the sand's actual
        // settled surface height - wins, since sand doesn't fill whole tiles the way solid blocks do.
        private bool HasVerticalWorldCollisionAt(WorldCollisionQuery collision, SandSystem sandSystem, Vector2 candidatePosition, int direction)
        {
            pendingVerticalLandingY = 0f;

            bool hasTileCollision = TryGetVerticalTileCollisionY(collision, candidatePosition, direction, out float tileLandingY);
            float sandLandingY = 0f;
            bool hasSandCollision = direction > 0
                && TryGetSandLandingY(sandSystem, kinematicMotor.Position, candidatePosition, out sandLandingY);

            if (!hasTileCollision && !hasSandCollision)
                return false;

            if (hasSandCollision && (!hasTileCollision || sandLandingY <= tileLandingY))
            {
                pendingVerticalLandingY = sandLandingY;
                return true;
            }

            pendingVerticalLandingY = tileLandingY;
            return true;
        }

        private bool HasGroundSupportAtCurrentPosition(WorldCollisionQuery collision, SandSystem sandSystem)
        {
            Vector2 probePosition = new(position.X, position.Y + 1f);
            bool hasTileSupport = TryGetVerticalTileCollisionY(collision, probePosition, 1, out float tileLandingY)
                && System.MathF.Abs(tileLandingY - position.Y) <= 0.001f;
            bool hasSandSupport = TryGetSandLandingY(sandSystem, position, probePosition, out float sandLandingY)
                && System.MathF.Abs(sandLandingY - position.Y) <= 0.001f;

            return hasTileSupport || hasSandSupport;
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

        private bool TryGetVerticalTileCollisionY(WorldCollisionQuery collision, Vector2 candidatePosition, int direction, out float landingY)
        {
            landingY = 0f;
            int ts = collision.TileSize;
            float left = GetHitLeft(candidatePosition) + 1f;
            float right = GetHitRight(candidatePosition) - 1f;
            int tileXLeft = (int)System.MathF.Floor(left / ts);
            int tileXRight = (int)System.MathF.Floor(right / ts);
            float edge = direction > 0
                ? GetHitBottom(candidatePosition)
                : GetHitTop(candidatePosition);
            int tileY = (int)System.MathF.Floor(edge / ts);

            if (!collision.HasBlockedInRow(tileY, tileXLeft, tileXRight))
                return false;

            landingY = direction > 0
                ? tileY * ts
                : (tileY * ts) + ts + config.HurtboxSize.Y - 1f;

            return true;
        }

        private bool TryGetSandLandingY(SandSystem sandSystem, Vector2 previousPosition, Vector2 candidatePosition, out float landingY)
        {
            landingY = 0f;
            if (sandSystem == null)
                return false;

            float previousBottom = GetHitBottom(previousPosition);
            float candidateBottom = GetHitBottom(candidatePosition);
            if (candidateBottom < previousBottom)
                return false;

            float fallDistance = System.MathF.Max(1f, candidateBottom - previousBottom);
            int minSupportX = (int)System.MathF.Floor(GetHitLeft(candidatePosition) + 1f);
            int maxSupportX = (int)System.MathF.Floor(GetHitRight(candidatePosition) - 1f);
            if (!sandSystem.TryGetSurfaceSupportY(
                minSupportX,
                maxSupportX,
                candidateBottom,
                fallDistance,
                MinSandSupportWidth,
                SandSurfaceHeightTolerance,
                out int sandSurfaceY))
                return false;

            if (sandSurfaceY < GetHitTop(candidatePosition) || previousBottom > sandSurfaceY || candidateBottom < sandSurfaceY)
                return false;

            landingY = sandSurfaceY;
            return true;
        }

        private float GetHitLeft(Vector2 candidatePosition) => candidatePosition.X - (config.HurtboxSize.X * 0.5f);
        private float GetHitRight(Vector2 candidatePosition) => GetHitLeft(candidatePosition) + config.HurtboxSize.X - 1f;
        private static float GetHitBottom(Vector2 candidatePosition) => candidatePosition.Y;
        private float GetHitTop(Vector2 candidatePosition) => GetHitBottom(candidatePosition) - config.HurtboxSize.Y + 1f;
    }
}
