using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Combat.Interfaces;
using Nyvorn.Source.Gameplay.Entities.Enemies.AI;
using Nyvorn.Source.Gameplay.Entities.Enemies.EnemyAnimations;
using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public class Enemy : IDamageable, IHitSource
    {
        private readonly EnemyConfig config;
        private readonly Texture2D texture;
        private readonly EnemyAnimator animator;
        private readonly EnemyBrain brain;
        private readonly EnemyCombat combat;
        private readonly EnemyMotor motor;

        public Vector2 Position => motor.Position;
        public bool IsAlive => combat.IsAlive;
        bool IHitSource.HasActiveHitbox => IsAlive && combat.AttackTimer > 0f;
        Rectangle IHitSource.ActiveHitbox => IsAlive && combat.AttackTimer > 0f ? Hurtbox : Rectangle.Empty;
        int IHitSource.HitSequence => 0;
        int IHitSource.HitDamage => config.ContactDamage;
        float IHitSource.HitKnockbackX => config.ContactKnockbackX;
        float IHitSource.HitKnockbackY => config.ContactKnockbackY;
        public int Health => combat.Health;
        public int MaxHealth => combat.MaxHealth;

        public Rectangle Hurtbox => motor.Hurtbox;

        public bool TryReceiveHit(Rectangle hitbox, int hitSequence, int damage)
        {
            return combat.TryReceiveHit(Hurtbox, hitbox, hitSequence, damage);
        }

        public Enemy(Texture2D texture, Vector2 position, EnemyConfig config = null)
        {
            this.config = config ?? EnemyConfig.Default;
            this.texture = texture;
            motor = new EnemyMotor(position, this.config);
            brain = new EnemyBrain(this.config);
            combat = new EnemyCombat(this.config);

            animator = new EnemyAnimator(EnemyTestAnimations.Create(), EnemyAnimState.Idle);
        }

        public void Update(float dt, WorldMap worldMap, Vector2 playerPosition)
        {
            combat.Tick(dt);
            EnemyBrainDecision decision = brain.Update(
                dt,
                Position,
                playerPosition,
                worldMap.PixelWidth,
                combat.Health,
                combat.MaxHealth);

            if (decision.TriggerAttackVisual)
                TriggerAttackVisual();

            motor.Update(dt, worldMap, decision.MoveVelocityX);

            EnemyAnimState state = ResolveAnimState();
            animator.Play(state);
            animator.Update(dt);
        }

        public void ApplyKnockback(float forceX, float forceY = -55f)
        {
            brain.NotifyHit(forceX);
            motor.ApplyKnockback(forceX, forceY);
        }

        public void ShiftX(float deltaX)
        {
            motor.ShiftX(deltaX);
        }

        public void TriggerAttackVisual(float? duration = null)
        {
            combat.TriggerAttackVisual(duration);
        }

        void IHitSource.OnHitConnected()
        {
            TriggerAttackVisual();
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!IsAlive)
                return;

            Rectangle src = animator.CurrentFrame;
            if (src == Rectangle.Empty)
                src = new Rectangle(0, config.FrameHeight, config.FrameWidth, config.FrameHeight);

            Vector2 origin = new Vector2(16f, 32f);
            spriteBatch.Draw(texture, Position, src, Color.White, 0f, origin, 1f, SpriteEffects.None, 0f);
        }

        private EnemyAnimState ResolveAnimState()
        {
            if (!IsAlive)
                return EnemyAnimState.Dead;

            if (combat.HurtTimer > 0f)
                return EnemyAnimState.Hurt;

            if (combat.AttackTimer > 0f)
                return EnemyAnimState.Attack;

            bool isMoving = Math.Abs(motor.HorizontalVelocityX) > 8f
                || brain.CurrentIntent == EnemyIntent.Chase
                || brain.CurrentIntent == EnemyIntent.Investigate
                || brain.CurrentIntent == EnemyIntent.Retreat;
            if (isMoving)
                return EnemyAnimState.Move;

            return EnemyAnimState.Idle;
        }

    }
}
