using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public interface IPlatform
    {
        bool IsPlatformBlockingMovement(Rectangle playerBounds, Vector2 playerVelocity);
    }
}
