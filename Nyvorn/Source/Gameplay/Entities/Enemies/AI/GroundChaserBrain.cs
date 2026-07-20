using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI
{
    // Deliberately simple, for a ground-walking enemy: notice the player once, then relentlessly
    // close the distance until actually touching them, jumping over whatever gets in the way (a
    // wall, a gap, or the player hopping over it). No scoring, no pathfinding.
    //
    // Aggro is gated by "isHostileTime" (day/night for surface enemies, always true for others -
    // see NightSurfaceSpawnSystem) and re-checked every frame rather than being permanent: if it
    // stops being hostile time mid-chase, the enemy drops the chase immediately and wanders instead,
    // it doesn't wait to lose sight of the player first.
    //
    // Damage itself isn't decided here at all - see the comment on Enemy.HitDamage for why contact
    // is the only thing that hurts you right now, and where a real per-enemy attack would plug in.
    public sealed class GroundChaserBrain
    {
        // How far ahead of its leading edge to check for missing ground before it walks off a ledge.
        private const float GapLookaheadDistance = 8f;

        private const float WanderSpeedFactor = 0.5f;
        private const float WanderWalkMinDuration = 1f;
        private const float WanderWalkMaxDuration = 3f;
        private const float WanderIdleMinDuration = 1.5f;
        private const float WanderIdleMaxDuration = 4f;

        private readonly EnemyConfig config;
        private readonly Random random = new();

        private bool hasNoticedPlayer;
        private bool isWanderWalking;
        private float wanderDirection;
        private float wanderStateTimer;

        public GroundChaserBrain(EnemyConfig config)
        {
            this.config = config;
        }

        public void NotifyHit(float knockbackX)
        {
            // Aggressive by design: getting hit doesn't make it flinch or back off, it just
            // guarantees aggro in case the hit came from outside the normal awareness range.
            hasNoticedPlayer = true;
        }

        // Night-spawned enemies are placed off-screen on purpose, which almost always puts them
        // outside PlayerAwarenessRange too - without this they'd stand frozen (isHostileTime is
        // true, but SeesTarget never fires from that far away) until the player wandered close
        // enough to be noticed organically. See NightSurfaceSpawnSystem.
        public void NoticePlayerImmediately()
        {
            hasNoticedPlayer = true;
        }

        public EnemyBrainDecision Update(
            in Perception perception, Vector2 selfPosition, WorldMap worldMap, bool isHostileTime, float dt)
        {
            if (!isHostileTime)
            {
                // Not tracked while passive: re-notices from scratch the next time it's hostile time,
                // same as a freshly spawned enemy would.
                hasNoticedPlayer = false;
                return UpdateWander(dt);
            }

            if (perception.SeesTarget)
                hasNoticedPlayer = true;

            if (!hasNoticedPlayer)
                return new EnemyBrainDecision(EnemyIntent.Idle, 0f, false);

            Vector2 offset = perception.TargetOffset;
            bool alreadyTouching = MathF.Abs(offset.X) <= config.ChaseStopDistance;
            float moveDirection = MathF.Sign(offset.X);
            float velocityX = alreadyTouching ? 0f : moveDirection * config.ChaseSpeed;

            // TODO once one-way platforms exist: descend through them here when the target is
            // below and reachable that way, instead of only ever jumping.
            bool wantsJump = !alreadyTouching
                && perception.OnGround
                && NeedsToJumpGap(selfPosition, moveDirection, worldMap);

            return new EnemyBrainDecision(EnemyIntent.Chase, velocityX, false, wantsJump);
        }

        // Passive daytime behavior: alternate between idling and strolling a short distance in a
        // random direction. No gap/wall awareness here on purpose - it's meant to look lazy, not
        // smart, and LocomotionController's reactive wall-bump jump still keeps it from getting
        // stuck against anything it wanders into.
        private EnemyBrainDecision UpdateWander(float dt)
        {
            wanderStateTimer -= dt;
            if (wanderStateTimer <= 0f)
            {
                isWanderWalking = !isWanderWalking;
                wanderStateTimer = isWanderWalking
                    ? RandomRange(WanderWalkMinDuration, WanderWalkMaxDuration)
                    : RandomRange(WanderIdleMinDuration, WanderIdleMaxDuration);

                if (isWanderWalking)
                    wanderDirection = random.Next(2) == 0 ? -1f : 1f;
            }

            float velocityX = isWanderWalking ? wanderDirection * config.ChaseSpeed * WanderSpeedFactor : 0f;
            return new EnemyBrainDecision(EnemyIntent.Idle, velocityX, false);
        }

        private float RandomRange(float min, float max)
        {
            return min + ((float)random.NextDouble() * (max - min));
        }

        private bool NeedsToJumpGap(Vector2 selfPosition, float moveDirection, WorldMap worldMap)
        {
            if (worldMap == null || moveDirection == 0f)
                return false;

            int tileSize = worldMap.TileSize;
            float aheadX = selfPosition.X + (moveDirection * ((config.HurtboxSize.X * 0.5f) + GapLookaheadDistance));
            int tileX = (int)MathF.Floor(aheadX / tileSize);
            int feetTileY = (int)MathF.Floor(selfPosition.Y / tileSize);

            return !worldMap.IsSolidAt(tileX, feetTileY);
        }
    }
}
