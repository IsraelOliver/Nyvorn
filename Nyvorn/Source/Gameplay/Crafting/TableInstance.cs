using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Interaction;
using Nyvorn.Source.Gameplay.World.Objects;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class TableInstance : IInteractable, IBaseSupportedWorldObject, IFurniture
    {
        private readonly int tileSize;

        public TableInstance(Point tile, int tileSize, bool facingLeft = false)
        {
            Tile = tile;
            this.tileSize = tileSize;
            FacingLeft = facingLeft;
        }

        public Point Tile { get; }
        public bool FacingLeft { get; }

        public Vector2 Position
        {
            get
            {
                int x = (Tile.X * tileSize) + ((tileSize - 24) / 2);
                int y = ((Tile.Y + 1) * tileSize) - 16;
                return new Vector2(x, y);
            }
        }

        public Vector2 InteractionPosition => Bounds.Center.ToVector2();

        public Rectangle Bounds => new Rectangle(
            (int)Position.X,
            (int)Position.Y,
            24,
            32);

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
