using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Interaction;
using Nyvorn.Source.Gameplay.World.Objects;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class FurnaceInstance : IInteractable, IBaseSupportedWorldObject, IFurniture
    {
        private readonly int tileSize;

        public FurnaceInstance(Point tile, int tileSize, bool facingLeft = false)
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
                int x = (Tile.X * tileSize) + ((tileSize - FurnaceRuntimeSystem.FurnaceWidth) / 2);
                int y = ((Tile.Y + 1) * tileSize) - FurnaceRuntimeSystem.FurnaceHeight;
                return new Vector2(x, y);
            }
        }

        public Vector2 InteractionPosition => Bounds.Center.ToVector2();

        public Rectangle Bounds => new Rectangle(
            (int)Position.X,
            (int)Position.Y,
            FurnaceRuntimeSystem.FurnaceWidth,
            FurnaceRuntimeSystem.FurnaceHeight);

        public bool CanInteract(Player player)
        {
            return player != null &&
                   Vector2.Distance(player.Position, InteractionPosition) <= player.WorldInteractionRange;
        }

        public InteractionResult Interact(Player player)
        {
            return CanInteract(player)
                ? InteractionResult.OpenHub(CraftTier.Furnace)
                : InteractionResult.None;
        }
    }
}
