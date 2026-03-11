using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Combat.Interfaces;
using Nyvorn.Source.Gameplay.Entities.Enemies.EnemyAnimations;
using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public class Enemy : IDamageable, IHitSource
    {
        private readonly Texture2D texture;
        private readonly EnemyAnimator animator;
        private readonly EnemyCombat combat;
        private readonly EnemyMotor motor;

        private const int FrameW = 32;
        private const int FrameH = 32;

        public Vector2 Position => motor.Position;
        public bool IsAlive => combat.IsAlive;
        bool IHitSource.HasActiveHitbox => IsAlive;
        Rectangle IHitSource.ActiveHitbox => IsAlive ? Hurtbox : Rectangle.Empty;
        int IHitSource.HitSequence => 0;
        public int Health => combat.Health;
        public int MaxHealth => combat.MaxHealth;

        public Rectangle Hurtbox => motor.Hurtbox;

        public bool TryReceiveHit(Rectangle hitbox, int hitSequence, int damage)
        {
            return combat.TryReceiveHit(Hurtbox, hitbox, hitSequence, damage);
        }

        public Enemy(Texture2D texture, Vector2 position, int maxHealth = 100)
        {
            this.texture = texture;
            motor = new EnemyMotor(position);
            combat = new EnemyCombat(maxHealth);

            animator = new EnemyAnimator(EnemyTestAnimations.Create(), EnemyAnimState.Idle);
        }

        public void Update(float dt, WorldMap worldMap)
        {
            combat.Tick(dt);
            motor.Update(dt, worldMap);

            EnemyAnimState state = ResolveAnimState();
            animator.Play(state);
            animator.Update(dt);
        }

        public void ApplyKnockback(float forceX, float forceY = -55f)
        {
            motor.ApplyKnockback(forceX, forceY);
        }

        public void TriggerAttackVisual(float duration = 0.12f)
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
                src = new Rectangle(0, 1 * FrameH, FrameW, FrameH);

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

            bool isMoving = Math.Abs(motor.KnockbackVelocityX) > 8f;
            if (isMoving)
                return EnemyAnimState.Move;

            return EnemyAnimState.Idle;
        }

    }
}
