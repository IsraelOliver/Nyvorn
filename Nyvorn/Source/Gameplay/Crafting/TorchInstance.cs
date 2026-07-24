using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.World.Objects;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class TorchInstance : IBaseSupportedWorldObject, IFurniture
    {
        private readonly int tileSize;

        public TorchInstance(Point tile, int poleFrameIndex, int tileSize, bool facingLeft = false)
        {
            Tile = tile;
            PoleFrameIndex = poleFrameIndex;
            this.tileSize = tileSize;
            FacingLeft = facingLeft;
        }

        public Point Tile { get; }
        public bool FacingLeft { get; }

        public Vector2 Position
        {
            get
            {
                int x = (Tile.X * tileSize) + ((tileSize - TorchRuntimeSystem.TorchWidth) / 2);
                int y = ((Tile.Y + 1) * tileSize) - TorchRuntimeSystem.TorchHeight;
                return new Vector2(x, y);
            }
        }

        // Which 8x8 frame of torch-Sheet this instance uses: 0/1 are the two ground variants, 2/3
        // are wall-mounted (left/right) - only 0/1 are ever placed today since wall mounting isn't
        // implemented yet, but TorchRuntimeSystem's anchor table already covers all four.
        public int PoleFrameIndex { get; }

        public Rectangle Bounds => new Rectangle(
            (int)Position.X,
            (int)Position.Y,
            TorchRuntimeSystem.TorchWidth,
            TorchRuntimeSystem.TorchHeight);

        // Where WorldLightingSystem seeds this torch's light from - the tile the flame sits in
        // (near the top of the bounds), not the base/anchor point.
        public Vector2 LightOrigin => new Vector2(Bounds.Center.X, Bounds.Top + 1f);
    }
}
