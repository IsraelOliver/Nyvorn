using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class PlayerMotor
    {
        private const float MaxSandStepHeight = 8f;
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

            float previousX = position.X;
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

            float yBeforeSandHorizontalResolution = position.Y;
            ResolveSandHorizontalMovement(worldMap, sandSystem, totalVelocityX, previousX);
            if (position.Y != yBeforeSandHorizontalResolution)
            {
                prevHitBottom = HitBottom;
                prevHitTop = HitTop;
                velocity.Y = 0f;
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

        private void ResolveSandHorizontalMovement(WorldMap worldMap, SandSystem sandSystem, float velocityX, float previousX)
        {
            if (!IsGrounded || sandSystem == null || velocityX == 0f)
                return;

            int moveDir = velocityX > 0f ? 1 : -1;
            if (!TryGetFrontSandSurfaceY(sandSystem, moveDir, out float sandSurfaceY))
                return;

            float rise = HitBottom - sandSurfaceY;
            if (rise <= 0f)
                return;

            if (rise > MaxSandStepHeight || !CanOccupyBottomAt(worldMap, sandSurfaceY))
            {
                position.X = previousX;
                velocity.X = 0f;
                knockbackVelocityX = 0f;
                return;
            }

            position.Y = sandSurfaceY;
            stepVisualOffsetY = System.MathF.Max(stepVisualOffsetY, rise);
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

            if (!worldMap.IsSolidAt(tileX, tileYBottom) || worldMap.IsSolidAt(tileX, tileYAbove))
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
            if (sandSystem == null || !TryGetSupportSandSurfaceY(sandSystem, out float sandSurfaceY))
                return false;

            if (sandSurfaceY < HitTop || prevHitBottom > sandSurfaceY || HitBottom < sandSurfaceY)
                return false;

            landingY = sandSurfaceY;
            return true;
        }

        private bool TryGetSupportSandSurfaceY(SandSystem sandSystem, out float surfaceY)
        {
            surfaceY = 0f;
            int[] probes = GetSandProbeXs();
            bool foundSurface = false;
            int bestSurfaceY = int.MaxValue;

            for (int i = 0; i < probes.Length; i++)
            {
                if (!sandSystem.TryGetWalkableSurfaceY(probes[i], probes[i], out int candidateSurfaceY) || candidateSurfaceY >= bestSurfaceY)
                    continue;

                bestSurfaceY = candidateSurfaceY;
                foundSurface = true;
            }

            if (!foundSurface)
                return false;

            surfaceY = bestSurfaceY;
            return true;
        }

        private bool TryGetFrontSandSurfaceY(SandSystem sandSystem, int moveDir, out float surfaceY)
        {
            surfaceY = 0f;
            int[] probes = GetSandProbeXs();
            int startIndex = moveDir > 0 ? 1 : 0;
            int endIndex = moveDir > 0 ? 2 : 1;
            bool foundSurface = false;
            int bestSurfaceY = int.MaxValue;

            for (int i = startIndex; i <= endIndex; i++)
            {
                if (!sandSystem.TryGetWalkableSurfaceY(probes[i], probes[i], out int candidateSurfaceY) || candidateSurfaceY >= bestSurfaceY)
                    continue;

                bestSurfaceY = candidateSurfaceY;
                foundSurface = true;
            }

            if (!foundSurface)
                return false;

            surfaceY = bestSurfaceY;
            return true;
        }

        private int[] GetSandProbeXs()
        {
            int leftProbe = (int)System.MathF.Floor(HitLeft + 1f);
            int centerProbe = (int)System.MathF.Floor((HitLeft + HitRight) * 0.5f);
            int rightProbe = (int)System.MathF.Floor(HitRight - 1f);
            return new[] { leftProbe, centerProbe, rightProbe };
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
                    if (worldMap.IsSolidAt(x, y))
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
                    if (worldMap.IsSolidAt(x, y))
                        return true;
                }
            }

            return false;
        }

        private bool HasSolidInColumn(WorldMap worldMap, int tileX, int tileYTop, int tileYBottom)
        {
            for (int y = tileYTop; y <= tileYBottom; y++)
            {
                if (worldMap.IsSolidAt(tileX, y))
                    return true;
            }

            return false;
        }

        private bool HasSolidInRow(WorldMap worldMap, int tileY, int tileXLeft, int tileXRight)
        {
            for (int x = tileXLeft; x <= tileXRight; x++)
            {
                if (worldMap.IsSolidAt(x, tileY))
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
