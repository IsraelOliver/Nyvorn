using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Combat.Interfaces
{
    public interface IHitSource
    {
        bool HasActiveHitbox { get; }
        Rectangle ActiveHitbox { get; }
        int HitSequence { get; }
        int HitDamage { get; }
        float HitKnockbackX { get; }
        float HitKnockbackY { get; }

        void OnHitConnected();
    }
}
