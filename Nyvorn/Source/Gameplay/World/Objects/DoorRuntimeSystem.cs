using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Interaction;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Persistence;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public sealed class DoorRuntimeSystem : IWorldObjectOccupancyProvider, IWorldObjectMovementBlocker, IForegroundTileBreakListener
    {
        public const int ClosedWidth = 8;
        public const int OpenWidth = 16;
        public const int DoorHeight = 24;

        private static readonly Rectangle ClosedSource = new Rectangle(0, 0, ClosedWidth, DoorHeight);
        private static readonly Rectangle OpenSource = new Rectangle(ClosedWidth, 0, OpenWidth, DoorHeight);
        private static readonly Color ValidPreviewTint = new Color(92, 255, 128, 140);
        private static readonly Color InvalidPreviewTint = new Color(255, 64, 64, 140);

        private readonly List<DoorInstance> doors = new();
        private Rectangle previewBounds;
        private bool previewVisible;
        private bool previewValid;
        private readonly RevisionTracker revisions = new();

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required Texture2D Texture { get; init; }

        public IReadOnlyList<DoorInstance> Doors => doors;
        public int Revision => revisions.Revision;
        public bool HasUnsavedChanges => revisions.HasUnsavedChanges;

        public void Restore(IEnumerable<DoorSaveData> savedDoors)
        {
            doors.Clear();
            if (savedDoors != null)
            {
                foreach (DoorSaveData savedDoor in savedDoors)
                {
                    if (savedDoor == null)
                        continue;

                    Point tile = new Point(WorldMap.WrapTileX(savedDoor.TileX), savedDoor.TileY);
                    if (!WorldMap.InBounds(tile.X, tile.Y))
                        continue;

                    doors.Add(new DoorInstance(tile, WorldMap.TileSize, savedDoor.IsOpen, savedDoor.OpensRight));
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public void MarkPersisted()
        {
            revisions.MarkPersisted();
        }

        public bool TryInteract(Player player)
        {
            if (!InteractionFinder.TryGetNearest(doors, player, out DoorInstance door))
                return false;

            bool toggled = door.TryToggle(player);
            if (toggled)
                revisions.MarkChanged();

            return true;
        }

        public bool TryPlaceSelectedDoor(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);

            if (!input.PlacePressed)
                return false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.WoodDoor)
                return false;

            Point baseTile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(baseTile.X, baseTile.Y))
                return true;

            if (!previewValid)
                return true;

            doors.Add(new DoorInstance(GetPlacementTopTile(baseTile), WorldMap.TileSize));
            revisions.MarkChanged();
            selectedSlot.RemoveOne();
            return true;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < doors.Count; i++)
            {
                DoorInstance door = doors[i];
                Rectangle source = door.IsOpen ? OpenSource : ClosedSource;
                Rectangle destination = door.DrawBounds;
                SpriteEffects effects = door.IsOpen && !door.OpensRight
                    ? SpriteEffects.FlipHorizontally
                    : SpriteEffects.None;

                spriteBatch.Draw(Texture, destination, source, Color.White, 0f, Vector2.Zero, effects, 0f);
            }

            if (previewVisible)
                spriteBatch.Draw(Texture, previewBounds, ClosedSource, previewValid ? ValidPreviewTint : InvalidPreviewTint);
        }

        public bool IsObjectOccupyingTile(int tileX, int tileY)
        {
            if (!WorldMap.InBounds(tileX, tileY))
                return false;

            Rectangle tileBounds = WorldMap.GetTileBounds(WorldMap.WrapTileX(tileX), tileY);
            for (int i = 0; i < doors.Count; i++)
            {
                if (doors[i].Bounds.Intersects(tileBounds))
                    return true;
            }

            return false;
        }

        public bool IsMovementBlockingTile(int tileX, int tileY)
        {
            if (!WorldMap.InBounds(tileX, tileY))
                return false;

            Rectangle tileBounds = WorldMap.GetTileBounds(WorldMap.WrapTileX(tileX), tileY);
            for (int i = 0; i < doors.Count; i++)
            {
                DoorInstance door = doors[i];
                if (!door.IsOpen && door.Bounds.Intersects(tileBounds))
                    return true;
            }

            return false;
        }

        public void RemoveDoorsAffectedByBrokenTile(Point tile, System.Action<DoorInstance> onDoorRemoved)
        {
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return;

            Point wrappedTile = new Point(WorldMap.WrapTileX(tile.X), tile.Y);
            bool removedAny = false;

            for (int i = doors.Count - 1; i >= 0; i--)
            {
                DoorInstance door = doors[i];
                if (!door.IsAffectedByBrokenForegroundTile(wrappedTile))
                    continue;

                onDoorRemoved?.Invoke(door);
                doors.RemoveAt(i);
                removedAny = true;
            }

            if (removedAny)
                revisions.MarkChanged();
        }

        public void OnForegroundTileBroken(ForegroundTileBrokenContext context)
        {
            RemoveDoorsAffectedByBrokenTile(context.Tile, door =>
                context.WorldItemRuntimeSystem.SpawnItemDrops(ItemId.WoodDoor, 1, door.InteractionPosition));
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            previewVisible = false;
            previewValid = false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.WoodDoor)
                return;

            Point baseTile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(baseTile.X, baseTile.Y))
                return;

            previewBounds = GetSnappedPlacementBounds(baseTile);
            previewVisible = true;
            previewValid = IsValidPlacement(previewBounds);
        }

        private Point GetPlacementTopTile(Point baseTile)
        {
            int doorTileHeight = DoorHeight / WorldMap.TileSize;
            return new Point(
                WorldMap.WrapTileX(baseTile.X),
                baseTile.Y - doorTileHeight + 1);
        }

        private Rectangle GetSnappedPlacementBounds(Point baseTile)
        {
            Point topTile = GetPlacementTopTile(baseTile);
            int x = topTile.X * WorldMap.TileSize;
            int y = topTile.Y * WorldMap.TileSize;
            return new Rectangle(x, y, ClosedWidth, DoorHeight);
        }

        private bool IsValidPlacement(Rectangle bounds)
        {
            return WorldObjectPlacementValidator.CanPlaceObject(WorldMap, Player, bounds) &&
                   HasDoorSupports(bounds);
        }

        private bool HasDoorSupports(Rectangle bounds)
        {
            int topSupportTileY = (bounds.Top / WorldMap.TileSize) - 1;
            int tileX = bounds.Left / WorldMap.TileSize;
            return WorldObjectSupport.HasFullBaseSupport(WorldMap, bounds) &&
                   WorldMap.IsSolidAt(tileX, topSupportTileY);
        }
    }
}
