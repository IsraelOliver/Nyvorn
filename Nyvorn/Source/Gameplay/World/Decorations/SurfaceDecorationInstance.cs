using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Decorations
{
    public sealed class SurfaceDecorationInstance
    {
        // Tile the decoration occupies (an air tile); the supporting solid tile is directly below.
        public required Point Tile { get; init; }
        public SurfaceDecorationType Type { get; init; }
        public int Seed { get; init; }
    }
}
