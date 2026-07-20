using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Physics.Sand;
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
        private readonly GroundChaserBrain brain;
        private readonly EnemyCombat combat;
        private readonly EnemyMotor motor;
        private readonly PerceptionComponent perceptionComponent;
        private readonly LocomotionController locomotion;

        public Vector2 Position => motor.Position;
        public bool IsAlive => combat.IsAlive;
        public EnemyConfig Config => config;
        bool IHitSource.HasActiveHitbox => IsAlive;
        Rectangle IHitSource.ActiveHitbox => IsAlive ? Hurtbox : Rectangle.Empty;
        int IHitSource.HitSequence => 0;

        // No dedicated attack yet: this enemy only ever deals ContactDamage, from its body touching
        // the player's. combat.AttackTimer still runs briefly after a successful hit (OnHitConnected
        // below) purely so there's a visual "attack" flash, but it doesn't change the damage amount.
        // A real attack (its own hitbox/reach, its own damage, a windup animation) will look different
        // per enemy type once that art/design exists, so it belongs on a per-enemy-type basis rather
        // than as a generic knob here.
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
            brain = new GroundChaserBrain(this.config);
            combat = new EnemyCombat(this.config);
            perceptionComponent = new PerceptionComponent();
            locomotion = new LocomotionController();

            animator = new EnemyAnimator(EnemyTestAnimations.Create(), EnemyAnimState.Idle);
        }

        public void Update(float dt, WorldMap worldMap, SandSystem sandSystem, Vector2 playerPosition, bool isHostileTime)
        {
            combat.Tick(dt);

            Perception perception = perceptionComponent.Scan(
                Position,
                playerPosition,
                worldMap.PixelWidth,
                motor.OnGround,
                motor.BlockedHorizontally,
                config);

            EnemyBrainDecision decision = brain.Update(perception, Position, worldMap, isHostileTime, dt);

            if (decision.TriggerAttackVisual)
                TriggerAttackVisual();

            locomotion.Apply(motor, animator, combat, decision, perception, dt, worldMap, sandSystem);
        }

        public void ApplyKnockback(float forceX, float forceY = -55f)
        {
            brain.NotifyHit(forceX);
            motor.ApplyKnockback(forceX, forceY);
        }

        public void NoticePlayerImmediately()
        {
            brain.NoticePlayerImmediately();
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
            spriteBatch.Draw(texture, Position, src, tint, 0f, origin, 1f, animator.Effects, 0f);
        }
    }
}
