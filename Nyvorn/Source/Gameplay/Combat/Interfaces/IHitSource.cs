using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Combat.Interfaces
{
    public interface IHitSource
    {
        bool HasActiveHitbox { get; }
        Rectangle ActiveHitbox { get; }
        int HitSequence { get; }

        void OnHitConnected();
    }
}
