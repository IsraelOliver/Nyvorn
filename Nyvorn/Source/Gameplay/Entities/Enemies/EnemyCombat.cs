using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyCombat
    {
        private const float AttackVisualDuration = 0.12f;
        private const float HurtDuration = 0.15f;

        private readonly int maxHealth;
        private int health;
        private int lastDamageHitSequence = -1;
        private float attackTimer;
        private float hurtTimer;

        public EnemyCombat(int maxHealth)
        {
            this.maxHealth = maxHealth;
            health = maxHealth;
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
            hurtTimer = HurtDuration;
            return true;
        }

        public void TriggerAttackVisual(float duration = AttackVisualDuration)
        {
            if (!IsAlive)
                return;

            if (duration > attackTimer)
                attackTimer = duration;
        }
    }
}
