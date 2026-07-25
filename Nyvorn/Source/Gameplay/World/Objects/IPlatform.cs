using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public interface IPlatform
    {
        bool IsPlatformBlockingMovement(Rectangle footSensor, Vector2 playerVelocity);
    }
}
