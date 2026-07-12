using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Interaction;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Persistence;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class FurnaceRuntimeSystem : IWorldObjectOccupancyProvider, IForegroundTileBreakListener, IWorldObjectMiningProvider
    {
        public const int FurnaceWidth = 24;
        public const int FurnaceHeight = 16;
        private const int HoverPadding = 8;

        private static readonly WorldObjectMiningDefinition MiningDefinition = new(true, 1.5f, 1);
        private static readonly Rectangle NormalSource = new Rectangle(0, 0, FurnaceWidth, FurnaceHeight);
        private static readonly Rectangle SelectedSource = new Rectangle(FurnaceWidth, 0, FurnaceWidth, FurnaceHeight);
        private static readonly Color ValidPreviewTint = new Color(92, 255, 128, 140);
        private static readonly Color InvalidPreviewTint = new Color(255, 64, 64, 140);

        private readonly List<FurnaceInstance> furnaces = new();
        private int hoveredFurnaceIndex = -1;
        private Rectangle previewBounds;
        private bool previewVisible;
        private bool previewValid;
        private readonly RevisionTracker revisions = new();

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required Texture2D Texture { get; init; }

        public IReadOnlyList<FurnaceInstance> Furnaces => furnaces;
        public bool HasUnsavedChanges => revisions.HasUnsavedChanges;

        public void Restore(IEnumerable<FurnaceSaveData> savedFurnaces)
        {
            furnaces.Clear();
            if (savedFurnaces != null)
            {
                foreach (FurnaceSaveData savedFurnace in savedFurnaces)
                {
                    if (savedFurnace != null)
                        furnaces.Add(new FurnaceInstance(new Vector2(savedFurnace.PositionX, savedFurnace.PositionY)));
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public void MarkPersisted()
        {
            revisions.MarkPersisted();
        }

        public void UpdateHover(Vector2 mouseWorld)
        {
            hoveredFurnaceIndex = -1;

            for (int i = 0; i < furnaces.Count; i++)
            {
                Rectangle bounds = furnaces[i].Bounds;
                Rectangle hoverBounds = bounds;
                hoverBounds.Inflate(HoverPadding, HoverPadding);
                if (!hoverBounds.Contains(mouseWorld))
                    continue;

                if (Vector2.Distance(Player.Position, bounds.Center.ToVector2()) > Player.WorldInteractionRange)
                    continue;

                hoveredFurnaceIndex = i;
                return;
            }
        }

        public CraftTier GetNearbyCraftTier()
        {
            return InteractionFinder.TryGetNearest(furnaces, Player, out _)
                ? CraftTier.Furnace
                : CraftTier.Basic;
        }

        public bool TryInteract(Player player, out InteractionResult result)
        {
            if (InteractionFinder.TryGetNearest(furnaces, player, out FurnaceInstance furnace))
            {
                result = furnace.Interact(player);
                return result != InteractionResult.None;
            }

            result = InteractionResult.None;
            return false;
        }

        public bool TryPlaceSelectedFurnace(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);

            if (!input.PlacePressed)
                return false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.Furnace)
                return false;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return true;

            Rectangle bounds = previewBounds;
            if (!previewValid)
                return true;

            furnaces.Add(new FurnaceInstance(new Vector2(bounds.X, bounds.Y)));
            revisions.MarkChanged();
            selectedSlot.RemoveOne();
            return true;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < furnaces.Count; i++)
            {
                Rectangle bounds = furnaces[i].Bounds;
                Rectangle source = i == hoveredFurnaceIndex ? SelectedSource : NormalSource;
                spriteBatch.Draw(Texture, bounds, source, Color.White);
            }

            if (previewVisible)
                spriteBatch.Draw(Texture, previewBounds, NormalSource, previewValid ? ValidPreviewTint : InvalidPreviewTint);
        }

        public bool IsObjectOccupyingTile(int tileX, int tileY)
        {
            return TryGetFurnaceIndexAtTile(new Point(tileX, tileY), out _);
        }

        public bool TryGetMiningTargetAtTile(Point tile, out WorldObjectMiningTarget target)
        {
            if (TryGetFurnaceIndexAtTile(tile, out int index))
            {
                target = new WorldObjectMiningTarget(MiningDefinition, furnaces[index].Bounds);
                return true;
            }

            target = default;
            return false;
        }

        public bool TryMineObjectAtTile(Point tile, WorldItemRuntimeSystem worldItemRuntimeSystem)
        {
            if (worldItemRuntimeSystem == null || !TryGetFurnaceIndexAtTile(tile, out int index))
                return false;

            FurnaceInstance furnace = furnaces[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.Furnace, 1, furnace.InteractionPosition);
            furnaces.RemoveAt(index);
            revisions.MarkChanged();
            return true;
        }

        public void RemoveFurnacesAffectedByBrokenTile(Point tile, System.Action<FurnaceInstance> onFurnaceRemoved)
        {
            if (WorldObjectSupport.RemoveObjectsWithBrokenBaseSupport(furnaces, tile, WorldMap, onFurnaceRemoved))
                revisions.MarkChanged();
        }

        public void OnForegroundTileBroken(ForegroundTileBrokenContext context)
        {
            RemoveFurnacesAffectedByBrokenTile(context.Tile, furnace =>
                context.WorldItemRuntimeSystem.SpawnItemDrops(ItemId.Furnace, 1, furnace.InteractionPosition));
        }

        private Rectangle GetSnappedPlacementBounds(Point tile)
        {
            int x = WorldMap.WrapTileX(tile.X) * WorldMap.TileSize;
            int y = tile.Y * WorldMap.TileSize;
            return new Rectangle(x, y, FurnaceWidth, FurnaceHeight);
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            previewVisible = false;
            previewValid = false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.Furnace)
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
            return WorldObjectPlacementValidator.CanPlaceObject(WorldMap, Player, bounds) &&
                   WorldObjectSupport.HasFullBaseSupport(WorldMap, bounds) &&
                   !IntersectsExistingFurnace(bounds);
        }

        private bool IntersectsExistingFurnace(Rectangle bounds)
        {
            for (int i = 0; i < furnaces.Count; i++)
            {
                if (furnaces[i].Bounds.Intersects(bounds))
                    return true;
            }

            return false;
        }

        private bool TryGetFurnaceIndexAtTile(Point tile, out int index)
        {
            index = -1;
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return false;

            Rectangle tileBounds = WorldMap.GetTileBounds(WorldMap.WrapTileX(tile.X), tile.Y);
            for (int i = 0; i < furnaces.Count; i++)
            {
                if (!furnaces[i].Bounds.Intersects(tileBounds))
                    continue;

                index = i;
                return true;
            }

            return false;
        }
    }
}
