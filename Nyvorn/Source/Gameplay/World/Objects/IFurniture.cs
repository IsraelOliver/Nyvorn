using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public interface IFurniture
    {
        Point Tile { get; }
        Vector2 Position { get; }
        Rectangle Bounds { get; }
        bool FacingLeft { get; }
    }
}
