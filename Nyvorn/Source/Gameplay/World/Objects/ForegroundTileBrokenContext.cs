using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public readonly struct ForegroundTileBrokenContext
    {
        public ForegroundTileBrokenContext(
            Point tile,
            TileType removedTile,
            Vector2 tileCenter,
            WorldItemRuntimeSystem worldItemRuntimeSystem)
        {
            Tile = tile;
            RemovedTile = removedTile;
            TileCenter = tileCenter;
            WorldItemRuntimeSystem = worldItemRuntimeSystem;
        }

        public Point Tile { get; }
        public TileType RemovedTile { get; }
        public Vector2 TileCenter { get; }
        public WorldItemRuntimeSystem WorldItemRuntimeSystem { get; }
    }
}
