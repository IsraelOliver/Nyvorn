using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Persistence;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public abstract class FurnitureRuntimeSystem<T> : IWorldObjectOccupancyProvider, IForegroundTileBreakListener, IWorldObjectMiningProvider
        where T : IFurniture
    {
        protected static readonly WorldObjectMiningDefinition DefaultMiningDefinition = new(true, 1.5f, 1);
        protected static readonly Color ValidPreviewTint = new Color(92, 255, 128, 140);
        protected static readonly Color InvalidPreviewTint = new Color(255, 64, 64, 140);

        protected readonly List<T> furnitureItems = new();
        protected Point previewBaseTile;
        protected bool previewVisible;
        protected bool previewValid;
        protected bool previewFacingLeft;
        protected readonly RevisionTracker revisions = new();

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }

        public IReadOnlyList<T> FurnitureItems => furnitureItems;
        public bool HasUnsavedChanges => revisions.HasUnsavedChanges;

        public void MarkPersisted()
        {
            revisions.MarkPersisted();
        }

        protected void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld, ItemId furnitureItemId, int previewWidth, int previewHeight)
        {
            previewVisible = false;
            previewValid = false;
            previewFacingLeft = !Player.FacingRight;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != furnitureItemId)
                return;

            Point baseTile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(baseTile.X, baseTile.Y))
                return;

            previewBaseTile = new Point(WorldMap.WrapTileX(baseTile.X), baseTile.Y);
            previewVisible = true;

            Rectangle previewBounds = GetPreviewBounds(previewBaseTile, previewWidth, previewHeight);
            previewValid = ValidatePlacement(previewBounds);
        }

        protected bool TryPlaceFurniture(InputState input, int selectedHotbarIndex, Vector2 mouseWorld, ItemId furnitureItemId, Action<Point> onPlace)
        {
            if (!input.PlacePressed)
                return false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != furnitureItemId)
                return false;

            Point baseTile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(baseTile.X, baseTile.Y))
                return true;

            if (!previewValid)
                return true;

            onPlace(previewBaseTile);
            revisions.MarkChanged();
            selectedSlot.RemoveOne();
            return true;
        }

        protected Rectangle GetPreviewBounds(Point tile, int width, int height)
        {
            int x = (tile.X * WorldMap.TileSize) + ((WorldMap.TileSize - width) / 2);
            int y = ((tile.Y + 1) * WorldMap.TileSize) - height;
            return new Rectangle(x, y, width, height);
        }

        protected bool ValidatePlacement(Rectangle bounds)
        {
            return WorldObjectPlacementValidator.CanPlaceObject(WorldMap, Player, bounds) &&
                   ValidateSupport(bounds) &&
                   !IntersectsExisting(bounds);
        }

        protected virtual bool ValidateSupport(Rectangle bounds)
        {
            return WorldObjectSupport.HasFullBaseSupport(WorldMap, bounds);
        }

        protected abstract bool IntersectsExisting(Rectangle bounds);

        public abstract bool IsObjectOccupyingTile(int tileX, int tileY);

        public abstract bool TryGetMiningTargetAtTile(Point tile, out WorldObjectMiningTarget target);

        public abstract bool TryMineObjectAtTile(Point tile, WorldItemRuntimeSystem worldItemRuntimeSystem);

        public abstract void OnForegroundTileBroken(ForegroundTileBrokenContext context);

        protected bool TryGetFurnitureIndexAtTile(Point tile, Func<T, bool> predicate, out int index)
        {
            index = -1;
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return false;

            Rectangle tileBounds = WorldMap.GetTileBounds(WorldMap.WrapTileX(tile.X), tile.Y);
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                if (!furnitureItems[i].Bounds.Intersects(tileBounds))
                    continue;

                if (!predicate(furnitureItems[i]))
                    continue;

                index = i;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Generic robust platform collision check. Works for any furniture used as platforms.
        /// Only blocks if player is moving downward and intersects the surface.
        /// </summary>
        protected bool CheckPlatformCollision(IReadOnlyList<T> items, Rectangle footSensor, Vector2 playerVelocity, Func<T, Rectangle> getSurfaceBounds)
        {
            // Only block if player is moving downward (falling/landing)
            if (playerVelocity.Y <= 0f)
                return false;

            // Check if player intersects any platform SURFACE
            for (int i = 0; i < items.Count; i++)
            {
                if (getSurfaceBounds(items[i]).Intersects(footSensor))
                    return true;
            }

            return false;
        }
    }
}
