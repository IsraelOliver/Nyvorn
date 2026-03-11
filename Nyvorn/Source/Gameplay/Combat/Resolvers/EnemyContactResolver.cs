using Nyvorn.Source.Gameplay.Combat.Interfaces;

namespace Nyvorn.Source.Gameplay.Combat.Resolvers
{
    public sealed class EnemyContactResolver
    {
        private readonly CombatConfig config;

        public EnemyContactResolver(CombatConfig config)
        {
            this.config = config;
        }

        public void Resolve<TSource, TTarget>(TSource source, TTarget target)
            where TSource : IDamageable, IHitSource
            where TTarget : IDamageable
        {
            if (!source.IsAlive || !target.IsAlive || !source.HasActiveHitbox)
                return;

            bool tookDamage = target.TryReceiveHit(source.ActiveHitbox, source.HitSequence, config.EnemyContactDamage);
            if (!tookDamage)
                return;

            source.OnHitConnected();
            float dir = target.Position.X >= source.Position.X ? 1f : -1f;
            target.ApplyKnockback(config.EnemyContactKnockbackX * dir, config.EnemyContactKnockbackY);
        }
    }
}
