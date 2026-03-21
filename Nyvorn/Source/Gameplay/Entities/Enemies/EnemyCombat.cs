using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyCombat
    {
        private readonly EnemyConfig config;
        private readonly int maxHealth;
        private int health;
        private int lastDamageHitSequence = -1;
        private float attackTimer;
        private float hurtTimer;

        public EnemyCombat(EnemyConfig config)
        {
            this.config = config;
            maxHealth = config.MaxHealth;
            health = config.MaxHealth;
            attackTimer = 0f;
            hurtTimer = 0f;
        }

        public bool IsAlive => health > 0;
        public int Health => health;
        public int MaxHealth => maxHealth;
        public float AttackTimer => attackTimer;
        public float HurtTimer => hurtTimer;

        public void Tick(float dt)
        {
            if (attackTimer > 0f)
                attackTimer -= dt;
            if (hurtTimer > 0f)
                hurtTimer -= dt;
        }

        public bool TryReceiveHit(Rectangle hurtbox, Rectangle hitbox, int hitSequence, int damage)
        {
            if (!IsAlive)
                return false;

            if (hitSequence == lastDamageHitSequence)
                return false;

            if (!hitbox.Intersects(hurtbox))
                return false;

            health = System.Math.Max(0, health - damage);
            lastDamageHitSequence = hitSequence;
            hurtTimer = config.HurtDuration;
            return true;
        }

        public void TriggerAttackVisual(float? duration = null)
        {
            if (!IsAlive)
                return;

            float visualDuration = duration ?? config.AttackVisualDuration;
            if (visualDuration > attackTimer)
                attackTimer = visualDuration;
        }
    }
}
