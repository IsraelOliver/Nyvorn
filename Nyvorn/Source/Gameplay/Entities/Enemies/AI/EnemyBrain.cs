using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI
{
    public sealed class EnemyBrain : IEnemyBrain
    {
        private readonly EnemyConfig config;

        private Vector2 lastKnownPlayerPosition;
        private float memoryTimer;
        private float retreatTimer;
        private float attackCooldownTimer;
        private float hitRetreatDirection;

        public EnemyBrain(EnemyConfig config)
        {
            this.config = config;
            CurrentIntent = EnemyIntent.Idle;
        }

        public EnemyIntent CurrentIntent { get; private set; }

        public void NotifyHit(float knockbackX)
        {
            retreatTimer = config.HitRetreatDuration;
            hitRetreatDirection = MathF.Sign(knockbackX);
            if (hitRetreatDirection == 0f)
                hitRetreatDirection = 1f;
        }

        public EnemyBrainDecision Update(
            float dt,
            in Perception perception,
            Vector2 selfPosition,
            float worldWidth,
            int health,
            int maxHealth,
            WorldMap worldMap)
        {
            if (attackCooldownTimer > 0f)
                attackCooldownTimer -= dt;
            if (memoryTimer > 0f)
                memoryTimer -= dt;
            if (retreatTimer > 0f)
                retreatTimer -= dt;

            Vector2 playerOffset = perception.TargetOffset;
            bool seesPlayer = perception.SeesTarget;

            if (seesPlayer)
            {
                lastKnownPlayerPosition = selfPosition + playerOffset;
                memoryTimer = config.PlayerMemoryDuration;
            }

            if (retreatTimer > 0f)
                return Decide(EnemyIntent.Retreat, hitRetreatDirection * config.RetreatSpeed, false);

            float healthPercent = maxHealth <= 0 ? 1f : health / (float)maxHealth;
            if (healthPercent <= config.LowHealthRetreatThreshold && seesPlayer && MathF.Abs(playerOffset.X) <= config.LowHealthRetreatRange)
            {
                float retreatDirection = -MathF.Sign(playerOffset.X);
                if (retreatDirection == 0f)
                    retreatDirection = hitRetreatDirection == 0f ? 1f : hitRetreatDirection;

                return Decide(EnemyIntent.Retreat, retreatDirection * config.RetreatSpeed, false);
            }

            if (seesPlayer && MathF.Abs(playerOffset.X) <= config.AttackRange && MathF.Abs(playerOffset.Y) <= config.AttackVerticalRange)
            {
                bool canAttack = attackCooldownTimer <= 0f;
                if (canAttack)
                    attackCooldownTimer = config.AttackCooldown;

                return Decide(EnemyIntent.Attack, 0f, canAttack);
            }

            if (seesPlayer)
            {
                float moveDirection = MathF.Sign(playerOffset.X);
                float speed = MathF.Abs(playerOffset.X) <= config.ChaseStopDistance ? 0f : moveDirection * config.ChaseSpeed;
                return Decide(EnemyIntent.Chase, speed, false);
            }

            if (memoryTimer > 0f)
            {
                Vector2 rememberedOffset = LoopAwareMath.GetOffset(selfPosition, lastKnownPlayerPosition, worldWidth);
                float speed = MathF.Abs(rememberedOffset.X) <= config.InvestigateStopDistance
                    ? 0f
                    : MathF.Sign(rememberedOffset.X) * config.InvestigateSpeed;

                return Decide(EnemyIntent.Investigate, speed, false);
            }

            return Decide(EnemyIntent.Idle, 0f, false);
        }

        private EnemyBrainDecision Decide(EnemyIntent intent, float moveVelocityX, bool triggerAttackVisual)
        {
            CurrentIntent = intent;
            return new EnemyBrainDecision(intent, moveVelocityX, triggerAttackVisual);
        }
    }
}
