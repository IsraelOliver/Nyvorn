using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Interaction;
using Nyvorn.Source.Gameplay.World.Objects;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class PlatformInstance : IInteractable, IBaseSupportedWorldObject, IFurniture
    {
        private const int PlatformWidth = 8;
        private const int PlatformHeight = 8;
        private readonly int tileSize;

        public PlatformInstance(Point tile, int tileSize)
        {
            Tile = tile;
            this.tileSize = tileSize;
        }

        public Point Tile { get; }
        public bool FacingLeft => false;

        public Vector2 Position
        {
            get
            {
                int x = Tile.X * tileSize;
                int y = (Tile.Y + 1) * tileSize - PlatformHeight;
                return new Vector2(x, y);
            }
        }

        public Vector2 InteractionPosition => Bounds.Center.ToVector2();

        public Rectangle Bounds => new Rectangle(
            (int)Position.X,
            (int)Position.Y,
            PlatformWidth,
            PlatformHeight);

        public Rectangle SurfaceBounds => new Rectangle(
            (int)Position.X,
            (int)Position.Y,
            PlatformWidth,
            2);

        public bool CanInteract(Player player)
        {
            return false;
        }

        public InteractionResult Interact(Player player)
        {
            return InteractionResult.None;
        }
    }
}
