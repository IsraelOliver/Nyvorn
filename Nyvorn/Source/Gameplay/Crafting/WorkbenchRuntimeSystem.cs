using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Interaction;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Persistence;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class WorkbenchRuntimeSystem
    {
        public const int WorkbenchWidth = 24;
        public const int WorkbenchHeight = 16;
        private const int HoverPadding = 8;

        private static readonly Rectangle NormalSource = new Rectangle(0, 0, WorkbenchWidth, WorkbenchHeight);
        private static readonly Rectangle SelectedSource = new Rectangle(WorkbenchWidth, 0, WorkbenchWidth, WorkbenchHeight);
        private static readonly Color ValidPreviewTint = new Color(92, 255, 128, 140);
        private static readonly Color InvalidPreviewTint = new Color(255, 64, 64, 140);

        private readonly List<WorkbenchInstance> workbenches = new();
        private int hoveredWorkbenchIndex = -1;
        private Rectangle previewBounds;
        private bool previewVisible;
        private bool previewValid;
        private int revision;
        private int persistedRevision;

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required Texture2D Texture { get; init; }

        public IReadOnlyList<WorkbenchInstance> Workbenches => workbenches;
        public bool HasUnsavedChanges => revision != persistedRevision;

        public void Restore(IEnumerable<WorkbenchSaveData> savedWorkbenches)
        {
            workbenches.Clear();
            if (savedWorkbenches != null)
            {
                foreach (WorkbenchSaveData savedWorkbench in savedWorkbenches)
                {
                    if (savedWorkbench != null)
                        workbenches.Add(new WorkbenchInstance(new Vector2(savedWorkbench.PositionX, savedWorkbench.PositionY)));
                }
            }

            revision++;
            MarkPersisted();
        }

        public void MarkPersisted()
        {
            persistedRevision = revision;
        }

        public void UpdateHover(Vector2 mouseWorld)
        {
            hoveredWorkbenchIndex = -1;

            for (int i = 0; i < workbenches.Count; i++)
            {
                Rectangle bounds = workbenches[i].Bounds;
                Rectangle hoverBounds = bounds;
                hoverBounds.Inflate(HoverPadding, HoverPadding);
                if (!hoverBounds.Contains(mouseWorld))
                    continue;

                if (Vector2.Distance(Player.Position, bounds.Center.ToVector2()) > Player.WorldInteractionRange)
                    continue;

                hoveredWorkbenchIndex = i;
                return;
            }
        }

        public CraftTier GetNearbyCraftTier()
        {
            return TryGetNearestInteractable(Player, out _)
                ? CraftTier.Workbench
                : CraftTier.Basic;
        }

        public bool TryInteract(Player player, out InteractionResult result)
        {
            if (TryGetNearestInteractable(player, out IInteractable interactable))
            {
                result = interactable.Interact(player);
                return result != InteractionResult.None;
            }

            result = InteractionResult.None;
            return false;
        }

        public bool TryPlaceSelectedWorkbench(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);

            if (!input.PlacePressed)
                return false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.Workbench)
                return false;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return true;

            Rectangle bounds = previewBounds;
            if (!previewValid)
                return true;

            workbenches.Add(new WorkbenchInstance(new Vector2(bounds.X, bounds.Y)));
            revision++;
            selectedSlot.RemoveOne();
            return true;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < workbenches.Count; i++)
            {
                Rectangle bounds = workbenches[i].Bounds;
                Rectangle source = i == hoveredWorkbenchIndex ? SelectedSource : NormalSource;
                spriteBatch.Draw(Texture, bounds, source, Color.White);
            }

            if (previewVisible)
                spriteBatch.Draw(Texture, previewBounds, NormalSource, previewValid ? ValidPreviewTint : InvalidPreviewTint);
        }

        public bool IsObjectOccupyingTile(int tileX, int tileY)
        {
            if (!WorldMap.InBounds(tileX, tileY))
                return false;

            Rectangle tileBounds = WorldMap.GetTileBounds(WorldMap.WrapTileX(tileX), tileY);
            for (int i = 0; i < workbenches.Count; i++)
            {
                if (workbenches[i].Bounds.Intersects(tileBounds))
                    return true;
            }

            return false;
        }

        private Rectangle GetSnappedPlacementBounds(Point tile)
        {
            int x = WorldMap.WrapTileX(tile.X) * WorldMap.TileSize;
            int y = tile.Y * WorldMap.TileSize;
            return new Rectangle(x, y, WorkbenchWidth, WorkbenchHeight);
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            previewVisible = false;
            previewValid = false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.Workbench)
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return;

            previewBounds = GetSnappedPlacementBounds(tile);
            previewVisible = true;
            previewValid = IsValidPlacement(previewBounds);
        }

        private bool IsValidPlacement(Rectangle bounds)
        {
            if (bounds.Intersects(Player.Hurtbox))
                return false;

            if (Vector2.Distance(Player.Position, bounds.Center.ToVector2()) > Player.WorldInteractionRange)
                return false;

            return CanOccupyGridArea(bounds) &&
                   HasExactGroundSupport(bounds) &&
                   !IntersectsExistingWorkbench(bounds);
        }

        private bool CanOccupyGridArea(Rectangle bounds)
        {
            int startTileX = bounds.Left / WorldMap.TileSize;
            int endTileX = (bounds.Right - 1) / WorldMap.TileSize;
            int startTileY = bounds.Top / WorldMap.TileSize;
            int endTileY = (bounds.Bottom - 1) / WorldMap.TileSize;

            for (int y = startTileY; y <= endTileY; y++)
            {
                if (!WorldMap.InBounds(startTileX, y))
                    return false;

                for (int x = startTileX; x <= endTileX; x++)
                {
                    if (WorldMap.IsSolidAt(x, y) || WorldMap.IsObjectOccupiedAt(x, y))
                        return false;
                }
            }

            return true;
        }

        private bool HasExactGroundSupport(Rectangle bounds)
        {
            int bottomTileY = bounds.Bottom / WorldMap.TileSize;
            int startTileX = bounds.Left / WorldMap.TileSize;
            int endTileX = (bounds.Right - 1) / WorldMap.TileSize;

            for (int x = startTileX; x <= endTileX; x++)
            {
                if (!WorldMap.IsSolidAt(x, bottomTileY))
                    return false;
            }

            return true;
        }

        private bool IntersectsExistingWorkbench(Rectangle bounds)
        {
            for (int i = 0; i < workbenches.Count; i++)
            {
                if (workbenches[i].Bounds.Intersects(bounds))
                    return true;
            }

            return false;
        }

        private bool TryGetNearestInteractable(Player player, out IInteractable interactable)
        {
            interactable = null;
            if (player == null)
                return false;

            float bestDistance = float.MaxValue;
            for (int i = 0; i < workbenches.Count; i++)
            {
                WorkbenchInstance workbench = workbenches[i];
                if (!workbench.CanInteract(player))
                    continue;

                float distance = Vector2.Distance(player.Position, workbench.InteractionPosition);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                interactable = workbench;
            }

            return interactable != null;
        }
    }
}
