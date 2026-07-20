using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.World.Objects;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class TorchInstance : IBaseSupportedWorldObject
    {
        public TorchInstance(Vector2 position)
        {
            Position = position;
        }

        public Vector2 Position { get; }

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
