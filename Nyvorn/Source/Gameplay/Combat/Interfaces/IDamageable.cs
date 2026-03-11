using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Combat.Interfaces
{
    public interface IDamageable
    {
        Vector2 Position { get; }
        Rectangle Hurtbox { get; }
        bool IsAlive { get; }

        bool TryReceiveHit(Rectangle hitbox, int hitSequence, int damage);
        void ApplyKnockback(float forceX, float forceY = -60f);
    }
}
