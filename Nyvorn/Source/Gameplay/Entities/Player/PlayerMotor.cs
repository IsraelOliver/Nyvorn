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
        private readonly KinematicBodyMotor kinematicMotor;

        private Vector2 position;
        private Vector2 velocity;
        private float knockbackVelocityX;
        private float stepVisualOffsetY;
        private Point currentHurtboxSize;
        private float pendingVerticalLandingY;

        public PlayerMotor(Vector2 startPosition, PlayerConfig config)
        {
            this.config = config;
            position = startPosition;
            kinematicMotor = new KinematicBodyMotor(startPosition);
            velocity = Vector2.Zero;
            currentHurtboxSize = config.HurtboxSize;
            IsGrounded = false;
        }

        public Vector2 Position
        {
            get => position;
            set
            {
                position = value;
                kinematicMotor.Position = value;
            }
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
            WorldCollisionQuery collision = WorldCollisionQuery.MovementBlockers(worldMap);
            UpdateHurtboxSize(collision, useDodgeHurtbox);

            bool wasGrounded = IsGrounded;

            velocity.X = desiredVelocityX;
            float totalVelocityX = velocity.X + knockbackVelocityX;
            float yBeforeHorizontalResolution = position.Y;
            MoveHorizontally(collision, totalVelocityX * dt);
            if (position.Y != yBeforeHorizontalResolution)
                velocity.Y = 0f;

            if (wasGrounded && TrySnapToSandSurface(collision, sandSystem, totalVelocityX))
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
            MoveVertically(collision, sandSystem, velocity.Y * dt);
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
            kinematicMotor.Reset(targetPosition);
            velocity = Vector2.Zero;
            knockbackVelocityX = 0f;
            stepVisualOffsetY = 0f;
            IsGrounded = false;
        }

        private void ApplyGravity(float dt)
        {
            velocity.Y += PhysicsSettings.WorldGravity * config.GravityScale * dt;
        }

        private void MoveHorizontally(WorldCollisionQuery collision, float amount)
        {
            kinematicMotor.Position = position;
            bool usedStepUp = false;

            bool HandleCollision(KinematicCollision hit)
            {
                position = kinematicMotor.Position;
                if (!usedStepUp && TryStepUp(collision, hit.Direction))
                {
                    usedStepUp = true;
                    kinematicMotor.Position = position;
                    return true;
                }

                velocity.X = 0f;
                knockbackVelocityX = 0f;
                return false;
            }

            kinematicMotor.MoveX(
                amount,
                (candidatePosition, axis, direction) => HasWorldCollisionAt(collision, candidatePosition, axis, direction),
                HandleCollision);

            position = kinematicMotor.Position;
        }

        private void MoveVertically(WorldCollisionQuery collision, SandSystem sandSystem, float amount)
        {
            IsGrounded = false;
            pendingVerticalLandingY = 0f;

            kinematicMotor.Position = position;

            bool HandleCollision(KinematicCollision hit)
            {
                kinematicMotor.Position = new Vector2(kinematicMotor.Position.X, pendingVerticalLandingY);
                position = kinematicMotor.Position;
                velocity.Y = 0f;
                IsGrounded = hit.Direction > 0;
                return false;
            }

            kinematicMotor.MoveY(
                amount,
                (candidatePosition, axis, direction) => HasVerticalWorldCollisionAt(collision, sandSystem, candidatePosition, direction),
                HandleCollision);

            position = kinematicMotor.Position;

            if (!IsGrounded && velocity.Y >= 0f && HasGroundSupportAtCurrentPosition(collision, sandSystem))
            {
                velocity.Y = 0f;
                IsGrounded = true;
                kinematicMotor.ClearRemainderY();
            }
        }

        private bool TrySnapToSandSurface(WorldCollisionQuery collision, SandSystem sandSystem, float velocityX)
        {
            if (!IsGrounded || sandSystem == null)
                return false;

            int moveDir = velocityX > 0f ? 1 : velocityX < 0f ? -1 : 0;
            if (!TryGetSandSurfaceForSnap(sandSystem, moveDir, out float sandSurfaceY))
                return false;

            float heightDelta = HitBottom - sandSurfaceY;
            if (heightDelta == 0f)
            {
                kinematicMotor.Position = position;
                kinematicMotor.ClearRemainderY();
                return true;
            }

            if (heightDelta > 0f)
            {
                if (!CanOccupyBottomAt(collision, sandSurfaceY))
                    return false;

                position.Y = sandSurfaceY;
                kinematicMotor.Position = position;
                kinematicMotor.ClearRemainderY();
                stepVisualOffsetY = System.MathF.Max(stepVisualOffsetY, heightDelta);
                return true;
            }

            if (!CanOccupyBottomAt(collision, sandSurfaceY))
                return false;

            position.Y = sandSurfaceY;
            kinematicMotor.Position = position;
            kinematicMotor.ClearRemainderY();
            return true;
        }

        private bool TryStepUp(WorldCollisionQuery collision, int moveDir)
        {
            if (!IsGrounded)
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

            stepVisualOffsetY = ts;
            return true;
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
                : (tileY * ts) + ts + currentHurtboxSize.Y - 1f;

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
            if (!TryGetStableSandSupportY(sandSystem, candidatePosition, candidateBottom, fallDistance, out float sandSurfaceY))
                return false;

            if (sandSurfaceY < GetHitTop(candidatePosition) || previousBottom > sandSurfaceY || candidateBottom < sandSurfaceY)
                return false;

            landingY = sandSurfaceY;
            return true;
        }

        private bool TryGetStableSandSupportY(SandSystem sandSystem, float referenceBottomY, float maxDistance, out float surfaceY)
            => TryGetStableSandSupportY(sandSystem, position, referenceBottomY, maxDistance, out surfaceY);

        private bool TryGetStableSandSupportY(SandSystem sandSystem, Vector2 candidatePosition, float referenceBottomY, float maxDistance, out float surfaceY)
        {
            surfaceY = 0f;
            int minSupportX = (int)System.MathF.Floor(GetHitLeft(candidatePosition) + 1f);
            int maxSupportX = (int)System.MathF.Floor(GetHitRight(candidatePosition) - 1f);
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

        private bool CanOccupyBottomAt(WorldCollisionQuery collision, float targetBottom)
        {
            int ts = collision.TileSize;
            float targetTop = targetBottom - currentHurtboxSize.Y + 1f;
            int tileXLeft = (int)System.MathF.Floor((HitLeft + 1f) / ts);
            int tileXRight = (int)System.MathF.Floor((HitRight - 1f) / ts);
            int tileYTop = (int)System.MathF.Floor(targetTop / ts);
            int tileYBottom = (int)System.MathF.Floor((targetBottom - 1f) / ts);

            return !collision.HasBlockedInArea(tileXLeft, tileXRight, tileYTop, tileYBottom);
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

        private bool HasWorldCollisionAt(WorldCollisionQuery collision, Vector2 candidatePosition, KinematicAxis axis, int direction)
        {
            if (axis == KinematicAxis.Horizontal)
                return HasHorizontalWorldCollisionAt(collision, candidatePosition, direction);

            return HasOverlapAt(collision, candidatePosition);
        }

        private bool HasHorizontalWorldCollisionAt(WorldCollisionQuery collision, Vector2 candidatePosition, int direction)
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

        private bool HasOverlapAt(WorldCollisionQuery collision, Vector2 candidatePosition)
        {
            int ts = collision.TileSize;
            int tileXLeft = (int)System.MathF.Floor((GetHitLeft(candidatePosition) + 1f) / ts);
            int tileXRight = (int)System.MathF.Floor((GetHitRight(candidatePosition) - 1f) / ts);
            int tileYTop = (int)System.MathF.Floor((GetHitTop(candidatePosition) + 1f) / ts);
            int tileYBottom = (int)System.MathF.Floor((GetHitBottom(candidatePosition) - 1f) / ts);

            return collision.HasBlockedInArea(tileXLeft, tileXRight, tileYTop, tileYBottom);
        }

        private float GetHitLeft(Vector2 candidatePosition) => candidatePosition.X - (currentHurtboxSize.X * 0.5f);
        private float GetHitRight(Vector2 candidatePosition) => GetHitLeft(candidatePosition) + currentHurtboxSize.X - 1f;
        private static float GetHitBottom(Vector2 candidatePosition) => candidatePosition.Y;
        private float GetHitTop(Vector2 candidatePosition) => GetHitBottom(candidatePosition) - currentHurtboxSize.Y + 1f;

        private void UpdateHurtboxSize(WorldCollisionQuery collision, bool useDodgeHurtbox)
        {
            Point targetSize = useDodgeHurtbox ? config.DodgeHurtboxSize : config.HurtboxSize;
            if (currentHurtboxSize == targetSize)
                return;

            Point previousSize = currentHurtboxSize;
            currentHurtboxSize = targetSize;

            if (useDodgeHurtbox)
                return;

            if (!HasBlockedOverlap(collision))
                return;

            currentHurtboxSize = previousSize;
        }
    }
}
