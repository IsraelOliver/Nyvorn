using Nyvorn.Source.Gameplay.Combat.Interfaces;

namespace Nyvorn.Source.Gameplay.Combat.Resolvers
{
    public sealed class PlayerAttackResolver
    {
        public void Resolve<TSource>(TSource source, IDamageable target)
            where TSource : IDamageable, IHitSource
        {
            if (!source.HasActiveHitbox || !target.IsAlive)
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
