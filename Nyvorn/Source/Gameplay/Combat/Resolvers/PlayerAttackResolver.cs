using Nyvorn.Source.Gameplay.Combat.Interfaces;

namespace Nyvorn.Source.Gameplay.Combat.Resolvers
{
    public sealed class PlayerAttackResolver
    {
        private readonly CombatConfig config;

        public PlayerAttackResolver(CombatConfig config)
        {
            this.config = config;
        }

        public void Resolve<TSource>(TSource source, IDamageable target)
            where TSource : IDamageable, IHitSource
        {
            if (!source.HasActiveHitbox || !target.IsAlive)
                return;

            bool tookDamage = target.TryReceiveHit(source.ActiveHitbox, source.HitSequence, config.PlayerAttackDamage);
            if (!tookDamage)
                return;

            source.OnHitConnected();
            float dir = target.Position.X >= source.Position.X ? 1f : -1f;
            target.ApplyKnockback(config.PlayerAttackKnockbackX * dir, config.PlayerAttackKnockbackY);
        }
    }
}
