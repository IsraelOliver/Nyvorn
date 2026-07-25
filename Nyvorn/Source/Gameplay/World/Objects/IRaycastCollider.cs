using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public interface IRaycastCollider
    {
        bool TryGetCollision(Vector2 previousPosition, Vector2 currentPosition, out CollisionRaycast collision);
    }
}
