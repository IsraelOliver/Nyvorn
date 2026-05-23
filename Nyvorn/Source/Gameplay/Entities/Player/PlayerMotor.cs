using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class PlayerMotor
    {
        private const float MaxSandStepHeight = 8f;
        private const int MinSandSupportWidth = 4;
        private const int MinSandTransitionSupportWidth = 3;
        private const int SandSurfaceHeightTolerance = 1;
        private readonly PlayerConfig config;

        private Vector2 position;
        private Vector2 velocity;
        private float knockbackVelocityX;
        private float stepVisualOffsetY;
        private Point currentHurtboxSize;

        public PlayerMotor(Vector2 startPosition, PlayerConfig config)
        {
            this.config = config;
            position = startPosition;
            velocity = Vector2.Zero;
            currentHurtboxSize = config.HurtboxSize;
            IsGrounded = false;
        }

        public Vector2 Position
        {
            get => position;
            set => position = value;
        }

        public Vector2 Velocity => velocity;
        public Vector2 VisualPosition => position + new Vector2(0f, stepVisualOffsetY);
        public bool IsGrounded { get; private set; }

        private float HitLeft => position.X - (currentHurtboxSize.X * 0.5f);
        private float HitRight => HitLeft + currentHurtboxSize.X - 1f;
        private float HitBottom => position.Y;
        private float HitTop => HitBottom - currentHurtboxSize.Y + 1f;

        public Rectangle Hurtbox => new Rectangle((int)HitLeft, (int)HitTop, currentHurtboxSize.X, currentHurtboxSize.Y);
        public float HitBottomValue => HitBottom;
        public float HitTopValue => HitTop;

        public void Update(float dt, WorldMap worldMap, SandSystem sandSystem, float desiredVelocityX, bool useDodgeHurtbox)
        {
            UpdateHurtboxSize(worldMap, useDodgeHurtbox);

            bool wasGrounded = IsGrounded;
            float prevHitBottom = HitBottom;
            float prevHitTop = HitTop;

            velocity.X = desiredVelocityX;
            float totalVelocityX = velocity.X + knockbackVelocityX;
            position.X += totalVelocityX * dt;
            float yBeforeHorizontalResolution = position.Y;
            ResolveWorldCollisionsX(worldMap, totalVelocityX);
            if (position.Y != yBeforeHorizontalResolution)
            {
                prevHitBottom = HitBottom;
                prevHitTop = HitTop;
                velocity.Y = 0f;
            }

            if (wasGrounded && TrySnapToSandSurface(worldMap, sandSystem, totalVelocityX))
            {
                velocity.Y = 0f;
                IsGrounded = true;
                knockbackVelocityX = MathHelper.Lerp(knockbackVelocityX, 0f, MathHelper.Clamp(dt * config.KnockbackRecovery, 0f, 1f));
                stepVisualOffsetY = MathHelper.Lerp(stepVisualOffsetY, 0f, MathHelper.Clamp(dt * 20f, 0f, 1f));
                return;
            }

            knockbackVelocityX = MathHelper.Lerp(knockbackVelocityX, 0f, MathHelper.Clamp(dt * config.KnockbackRecovery, 0f, 1f));
            stepVisualOffsetY = MathHelper.Lerp(stepVisualOffsetY, 0f, MathHelper.Clamp(dt * 20f, 0f, 1f));

            ApplyGravity(dt);
            position.Y += velocity.Y * dt;
            ResolveWorldCollisionsY(worldMap, sandSystem, prevHitBottom, prevHitTop);
        }

        public void TryJump()
        {
            if (!IsGrounded)
                return;

            velocity.Y = -config.JumpSpeed;
            IsGrounded = false;
        }

        public void ApplyKnockback(float forceX, float forceY)
        {
            knockbackVelocityX = forceX;
            if (forceY < velocity.Y)
                velocity.Y = forceY;
        }

        public void TeleportTo(Vector2 targetPosition)
        {
            position = targetPosition;
            velocity = Vector2.Zero;
            knockbackVelocityX = 0f;
            stepVisualOffsetY = 0f;
            IsGrounded = false;
        }

        private void ApplyGravity(float dt)
        {
            velocity.Y += PhysicsSettings.WorldGravity * config.GravityScale * dt;
        }

        private void ResolveWorldCollisionsX(WorldMap worldMap, float velocityX)
        {
            int ts = worldMap.TileSize;
            float top = HitTop + 1;
            float bottom = HitBottom - 1;
            int tileYTop = (int)System.MathF.Floor(top / ts);
            int tileYBottom = (int)System.MathF.Floor(bottom / ts);

            if (velocityX > 0)
            {
                float right = HitRight;
                int tileX = (int)System.MathF.Floor(right / ts);

                if (HasSolidInColumn(worldMap, tileX, tileYTop, tileYBottom))
                {
                    if (TryStepUp(worldMap, 1))
                        return;

                    float tileLeft = tileX * ts;
                    float newHitLeft = tileLeft - currentHurtboxSize.X;
                    position.X = newHitLeft + (currentHurtboxSize.X * 0.5f);
                    velocity.X = 0f;
                    knockbackVelocityX = 0f;
                }
            }
            else if (velocityX < 0)
            {
                float left = HitLeft;
                int tileX = (int)System.MathF.Floor(left / ts);

                if (HasSolidInColumn(worldMap, tileX, tileYTop, tileYBottom))
                {
                    if (TryStepUp(worldMap, -1))
                        return;

                    float tileRight = tileX * ts + ts;
                    float newHitLeft = tileRight;
                    position.X = newHitLeft + (currentHurtboxSize.X * 0.5f);
                    velocity.X = 0f;
                    knockbackVelocityX = 0f;
                }
            }
        }

        private void ResolveWorldCollisionsY(WorldMap worldMap, SandSystem sandSystem, float prevHitBottom, float prevHitTop)
        {
            IsGrounded = false;

            int ts = worldMap.TileSize;
            float left = HitLeft + 1;
            float right = HitRight - 1;
            int tileXLeft = (int)(left / ts);
            int tileXRight = (int)(right / ts);

            if (velocity.Y > 0)
            {
                bool hasTileLanding = TryGetTileLandingY(worldMap, prevHitBottom, HitBottom, tileXLeft, tileXRight, out float tileLandingY);
                bool hasSandLanding = TryGetSandLandingY(sandSystem, prevHitBottom, out float sandLandingY);

                if (!hasTileLanding && !hasSandLanding)
                    return;

                float landingY = hasTileLanding && hasSandLanding
                    ? System.MathF.Min(tileLandingY, sandLandingY)
                    : (hasTileLanding ? tileLandingY : sandLandingY);

                position.Y = landingY;
                velocity.Y = 0f;
                IsGrounded = true;
                return;
            }
            else if (velocity.Y < 0)
            {
                int fromY = (int)(prevHitTop / ts);
                int toY = (int)(HitTop / ts);

                for (int y = fromY; y >= toY; y--)
                {
                    if (HasSolidInRow(worldMap, y, tileXLeft, tileXRight))
                    {
                        float tileBottom = y * ts + ts;
                        position.Y = tileBottom + currentHurtboxSize.Y - 1;
                        velocity.Y = 0f;
                        return;
                    }
                }
            }
        }

        private bool TrySnapToSandSurface(WorldMap worldMap, SandSystem sandSystem, float velocityX)
        {
            if (!IsGrounded || sandSystem == null)
                return false;

            int moveDir = velocityX > 0f ? 1 : velocityX < 0f ? -1 : 0;
            if (!TryGetSandSurfaceForSnap(sandSystem, moveDir, out float sandSurfaceY))
                return false;

            float heightDelta = HitBottom - sandSurfaceY;
            if (heightDelta == 0f)
                return true;

            if (heightDelta > 0f)
            {
                if (!CanOccupyBottomAt(worldMap, sandSurfaceY))
                    return false;

                position.Y = sandSurfaceY;
                stepVisualOffsetY = System.MathF.Max(stepVisualOffsetY, heightDelta);
                return true;
            }

            if (!CanOccupyBottomAt(worldMap, sandSurfaceY))
                return false;

            position.Y = sandSurfaceY;
            return true;
        }

        private bool TryStepUp(WorldMap worldMap, int moveDir)
        {
            if (!IsGrounded)
                return false;

            int ts = worldMap.TileSize;
            float frontX = moveDir > 0 ? HitRight + 1f : HitLeft - 1f;
            int tileX = (int)System.MathF.Floor(frontX / ts);
            int tileYBottom = (int)System.MathF.Floor((HitBottom - 1f) / ts);
            int tileYAbove = tileYBottom - 1;

            if (!worldMap.IsMovementBlockedAt(tileX, tileYBottom) || worldMap.IsMovementBlockedAt(tileX, tileYAbove))
                return false;

            float originalY = position.Y;
            position.Y -= ts;

            if (HasSolidOverlap(worldMap))
            {
                position.Y = originalY;
                return false;
            }

            stepVisualOffsetY = ts;
            return true;
        }

        private bool TryGetTileLandingY(WorldMap worldMap, float prevHitBottom, float currentHitBottom, int tileXLeft, int tileXRight, out float landingY)
        {
            landingY = 0f;

            int ts = worldMap.TileSize;
            int fromY = (int)(prevHitBottom / ts);
            int toY = (int)(currentHitBottom / ts);

            for (int y = fromY; y <= toY; y++)
            {
                if (!HasSolidInRow(worldMap, y, tileXLeft, tileXRight))
                    continue;

                landingY = y * ts;
                return true;
            }

            return false;
        }

        private bool TryGetSandLandingY(SandSystem sandSystem, float prevHitBottom, out float landingY)
        {
            landingY = 0f;
            float fallDistance = System.MathF.Max(1f, HitBottom - prevHitBottom);
            if (sandSystem == null || !TryGetStableSandSupportY(sandSystem, HitBottom, fallDistance, out float sandSurfaceY))
                return false;

            if (sandSurfaceY < HitTop || prevHitBottom > sandSurfaceY || HitBottom < sandSurfaceY)
                return false;

            landingY = sandSurfaceY;
            return true;
        }

        private bool TryGetStableSandSupportY(SandSystem sandSystem, float referenceBottomY, float maxDistance, out float surfaceY)
        {
            surfaceY = 0f;
            int minSupportX = (int)System.MathF.Floor(HitLeft + 1f);
            int maxSupportX = (int)System.MathF.Floor(HitRight - 1f);
            if (!sandSystem.TryGetSurfaceSupportY(
                minSupportX,
                maxSupportX,
                referenceBottomY,
                maxDistance,
                MinSandSupportWidth,
                SandSurfaceHeightTolerance,
                out int bestSurfaceY))
                return false;

            surfaceY = bestSurfaceY;
            return true;
        }

        private bool TryGetSandSurfaceForSnap(SandSystem sandSystem, int moveDir, out float surfaceY)
        {
            surfaceY = 0f;

            GetSandSnapRange(moveDir, out int minSupportX, out int maxSupportX, out int minSupportWidth);

            if (!sandSystem.TryGetSurfaceSupportY(
                minSupportX,
                maxSupportX,
                HitBottom,
                MaxSandStepHeight,
                minSupportWidth,
                SandSurfaceHeightTolerance,
                out int bestSurfaceY))
                return false;

            surfaceY = bestSurfaceY;
            return true;
        }

        private void GetSandSnapRange(int moveDir, out int minSupportX, out int maxSupportX, out int minSupportWidth)
        {
            if (moveDir > 0)
            {
                minSupportX = (int)System.MathF.Floor((HitLeft + HitRight) * 0.5f);
                maxSupportX = (int)System.MathF.Floor(HitRight - 1f);
                minSupportWidth = MinSandTransitionSupportWidth;
                return;
            }

            if (moveDir < 0)
            {
                minSupportX = (int)System.MathF.Floor(HitLeft + 1f);
                maxSupportX = (int)System.MathF.Floor((HitLeft + HitRight) * 0.5f);
                minSupportWidth = MinSandTransitionSupportWidth;
                return;
            }

            minSupportX = (int)System.MathF.Floor(HitLeft + 1f);
            maxSupportX = (int)System.MathF.Floor(HitRight - 1f);
            minSupportWidth = MinSandSupportWidth;
        }

        private bool CanOccupyBottomAt(WorldMap worldMap, float targetBottom)
        {
            int ts = worldMap.TileSize;
            float targetTop = targetBottom - currentHurtboxSize.Y + 1f;
            int tileXLeft = (int)System.MathF.Floor((HitLeft + 1f) / ts);
            int tileXRight = (int)System.MathF.Floor((HitRight - 1f) / ts);
            int tileYTop = (int)System.MathF.Floor(targetTop / ts);
            int tileYBottom = (int)System.MathF.Floor((targetBottom - 1f) / ts);

            for (int y = tileYTop; y <= tileYBottom; y++)
            {
                for (int x = tileXLeft; x <= tileXRight; x++)
                {
                    if (worldMap.IsMovementBlockedAt(x, y))
                        return false;
                }
            }

            return true;
        }

        private bool HasSolidOverlap(WorldMap worldMap)
        {
            int ts = worldMap.TileSize;
            int tileXLeft = (int)System.MathF.Floor((HitLeft + 1f) / ts);
            int tileXRight = (int)System.MathF.Floor((HitRight - 1f) / ts);
            int tileYTop = (int)System.MathF.Floor(HitTop / ts);
            int tileYBottom = (int)System.MathF.Floor((HitBottom - 1f) / ts);

            for (int y = tileYTop; y <= tileYBottom; y++)
            {
                for (int x = tileXLeft; x <= tileXRight; x++)
                {
                    if (worldMap.IsMovementBlockedAt(x, y))
                        return true;
                }
            }

            return false;
        }

        private bool HasSolidInColumn(WorldMap worldMap, int tileX, int tileYTop, int tileYBottom)
        {
            for (int y = tileYTop; y <= tileYBottom; y++)
            {
                if (worldMap.IsMovementBlockedAt(tileX, y))
                    return true;
            }

            return false;
        }

        private bool HasSolidInRow(WorldMap worldMap, int tileY, int tileXLeft, int tileXRight)
        {
            for (int x = tileXLeft; x <= tileXRight; x++)
            {
                if (worldMap.IsMovementBlockedAt(x, tileY))
                    return true;
            }

            return false;
        }

        private void UpdateHurtboxSize(WorldMap worldMap, bool useDodgeHurtbox)
        {
            Point targetSize = useDodgeHurtbox ? config.DodgeHurtboxSize : config.HurtboxSize;
            if (currentHurtboxSize == targetSize)
                return;

            Point previousSize = currentHurtboxSize;
            currentHurtboxSize = targetSize;

            if (useDodgeHurtbox)
                return;

            if (!HasSolidOverlap(worldMap))
                return;

            currentHurtboxSize = previousSize;
        }
    }
}
