using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Items;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public readonly struct ForegroundTileBrokenContext
    {
        public ForegroundTileBrokenContext(
            Point tile,
            WorldItemRuntimeSystem worldItemRuntimeSystem)
        {
            Tile = tile;
            WorldItemRuntimeSystem = worldItemRuntimeSystem;
        }

        public Point Tile { get; }
        public WorldItemRuntimeSystem WorldItemRuntimeSystem { get; }
    }
}
