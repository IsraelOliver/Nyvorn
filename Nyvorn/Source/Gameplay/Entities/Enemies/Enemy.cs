using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Combat.Interfaces;
using Nyvorn.Source.Gameplay.Entities.Enemies.AI;
using Nyvorn.Source.Gameplay.Entities.Enemies.EnemyAnimations;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public class Enemy : IDamageable, IHitSource
    {
        private readonly EnemyConfig config;
        private readonly Texture2D texture;
        private readonly EnemyAnimator animator;
        private readonly IEnemyBrain brain;
        private readonly EnemyCombat combat;
        private readonly EnemyMotor motor;
        private readonly PerceptionComponent perceptionComponent;
        private readonly LocomotionController locomotion;

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
            brain = this.config.Brain == BrainType.Utility ? new UtilityBrain(this.config) : new EnemyBrain(this.config);
            combat = new EnemyCombat(this.config);
            perceptionComponent = new PerceptionComponent();
            locomotion = new LocomotionController();

            animator = new EnemyAnimator(EnemyTestAnimations.Create(), EnemyAnimState.Idle);
        }

        public void Update(float dt, WorldMap worldMap, Vector2 playerPosition)
        {
            combat.Tick(dt);

            Perception perception = perceptionComponent.Scan(
                Position,
                playerPosition,
                worldMap.PixelWidth,
                motor.OnGround,
                motor.BlockedHorizontally,
                config);

            EnemyBrainDecision decision = brain.Update(
                dt,
                perception,
                Position,
                worldMap.PixelWidth,
                combat.Health,
                combat.MaxHealth,
                worldMap);

            if (decision.TriggerAttackVisual)
                TriggerAttackVisual();

            locomotion.Apply(motor, animator, combat, decision, perception, dt, worldMap);
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

        public void Draw(SpriteBatch spriteBatch, Color tint)
        {
            if (!IsAlive)
                return;

            Rectangle src = animator.CurrentFrame;
            if (src == Rectangle.Empty)
                src = new Rectangle(0, config.FrameHeight, config.FrameWidth, config.FrameHeight);

            Vector2 origin = new Vector2(16f, 32f);
            spriteBatch.Draw(texture, Position, src, tint, 0f, origin, 1f, SpriteEffects.None, 0f);
        }
    }
}
