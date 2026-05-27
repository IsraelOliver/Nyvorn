using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Interaction;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public sealed class DoorInstance : IInteractable
    {
        private readonly int tileSize;

        public DoorInstance(Point tile, int tileSize, bool isOpen = false, bool opensRight = true)
        {
            Tile = tile;
            this.tileSize = tileSize;
            IsOpen = isOpen;
            OpensRight = opensRight;
        }

        public Point Tile { get; }
        public bool IsOpen { get; private set; }
        public bool OpensRight { get; private set; }

        public Vector2 Position => new Vector2(Tile.X * tileSize, Tile.Y * tileSize);
        public Vector2 InteractionPosition => Bounds.Center.ToVector2();
        public Point SupportTile => new Point(Tile.X, Tile.Y + (DoorRuntimeSystem.DoorHeight / tileSize));

        public Rectangle Bounds => new Rectangle(
            Tile.X * tileSize,
            Tile.Y * tileSize,
            DoorRuntimeSystem.ClosedWidth,
            DoorRuntimeSystem.DoorHeight);

        public Rectangle DrawBounds
        {
            get
            {
                if (!IsOpen)
                    return Bounds;

                Rectangle closedBounds = Bounds;
                int x = OpensRight
                    ? closedBounds.X
                    : closedBounds.Right - DoorRuntimeSystem.OpenWidth;

                return new Rectangle(x, closedBounds.Y, DoorRuntimeSystem.OpenWidth, DoorRuntimeSystem.DoorHeight);
            }
        }

        public bool CanInteract(Player player)
        {
            return player != null &&
                   Vector2.Distance(player.Position, InteractionPosition) <= player.WorldInteractionRange;
        }

        public bool TryToggle(Player player)
        {
            if (!CanInteract(player))
                return false;

            if (IsOpen)
            {
                if (Bounds.Intersects(player.Hurtbox))
                    return false;

                IsOpen = false;
                return true;
            }

            OpensRight = player.Position.X <= InteractionPosition.X;
            IsOpen = true;
            return true;
        }

        public bool IsAffectedByBrokenForegroundTile(Point tile)
        {
            if (tile == SupportTile)
                return true;

            Rectangle tileBounds = new Rectangle(tile.X * tileSize, tile.Y * tileSize, tileSize, tileSize);
            return Bounds.Intersects(tileBounds);
        }

        public InteractionResult Interact(Player player)
        {
            TryToggle(player);
            return InteractionResult.None;
        }
    }
}
