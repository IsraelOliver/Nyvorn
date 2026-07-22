using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.World.Objects;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class TorchInstance : IBaseSupportedWorldObject
    {
        public TorchInstance(Vector2 position, int poleFrameIndex)
        {
            Position = position;
            PoleFrameIndex = poleFrameIndex;
        }

        public Vector2 Position { get; }

        // Which 8x8 frame of torch-Sheet this instance uses: 0/1 are the two ground variants, 2/3
        // are wall-mounted (left/right) - only 0/1 are ever placed today since wall mounting isn't
        // implemented yet, but TorchRuntimeSystem's anchor table already covers all four.
        public int PoleFrameIndex { get; }

        public Rectangle Bounds => new Rectangle(
            (int)System.MathF.Round(Position.X),
            (int)System.MathF.Round(Position.Y),
            TorchRuntimeSystem.TorchWidth,
            TorchRuntimeSystem.TorchHeight);

        // Where WorldLightingSystem seeds this torch's light from - the tile the flame sits in
        // (near the top of the bounds), not the base/anchor point.
        public Vector2 LightOrigin => new Vector2(Bounds.Center.X, Bounds.Top + 1f);
    }
}
