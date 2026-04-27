using Nyvorn.Source.Gameplay.Combat.Interfaces;

namespace Nyvorn.Source.Gameplay.Combat.Resolvers
{
    public sealed class EnemyContactResolver
    {
        public void Resolve<TSource, TTarget>(TSource source, TTarget target)
            where TSource : IDamageable, IHitSource
            where TTarget : IDamageable
        {
            if (!source.IsAlive || !target.IsAlive || !source.HasActiveHitbox)
                return;

            bool tookDamage = target.TryReceiveHit(source.ActiveHitbox, source.HitSequence, source.HitDamage);
            if (!tookDamage)
                return;

            source.OnHitConnected();
            float dir = target.Position.X >= source.Position.X ? 1f : -1f;
            target.ApplyKnockback(source.HitKnockbackX * dir, source.HitKnockbackY);
        }
    }
}
