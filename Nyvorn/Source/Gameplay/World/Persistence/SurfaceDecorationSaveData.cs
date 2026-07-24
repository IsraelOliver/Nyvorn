using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Decorations;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class SurfaceDecorationSaveData
    {
        public int TileX { get; init; }
        public int TileY { get; init; }
        public SurfaceDecorationType Type { get; init; }
        public int Seed { get; init; }

        public static SurfaceDecorationSaveData FromDecoration(SurfaceDecorationInstance decoration)
        {
            return new SurfaceDecorationSaveData
            {
                TileX = decoration.Tile.X,
                TileY = decoration.Tile.Y,
                Type = decoration.Type,
                Seed = decoration.Seed
            };
        }

        public SurfaceDecorationInstance ToDecoration()
        {
            return new SurfaceDecorationInstance
            {
                Tile = new Point(TileX, TileY),
                Type = Type,
                Seed = Seed
            };
        }
    }
}
