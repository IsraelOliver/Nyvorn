using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Persistence;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Crafting
{
    // Placeholder visuals: no dedicated torch sprite exists yet, so this reuses the furnace texture
    // tinted orange, stretched into a small torch-shaped box. Swap PlaceholderSource/PlaceholderTint
    // for a real sprite once art exists - nothing else here depends on the texture being a furnace.
    public sealed class TorchRuntimeSystem : IForegroundTileBreakListener, IWorldObjectMiningProvider
    {
        public const int TorchWidth = 6;
        public const int TorchHeight = 12;

        private static readonly WorldObjectMiningDefinition MiningDefinition = new(true, 0.5f, 1);
        private static readonly Rectangle PlaceholderSource = new Rectangle(0, 0, FurnaceRuntimeSystem.FurnaceWidth, FurnaceRuntimeSystem.FurnaceHeight);
        private static readonly Color PlaceholderTint = new Color(255, 150, 60);
        private static readonly Color ValidPreviewTint = new Color(92, 255, 128, 140);
        private static readonly Color InvalidPreviewTint = new Color(255, 64, 64, 140);

        private readonly List<TorchInstance> torches = new();
        private Rectangle previewBounds;
        private bool previewVisible;
        private bool previewValid;
        private readonly RevisionTracker revisions = new();

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required Texture2D Texture { get; init; }

        public IReadOnlyList<TorchInstance> Torches => torches;
        public bool HasUnsavedChanges => revisions.HasUnsavedChanges;

        // Consumed by WorldLightingSystem each frame - see PlayingSession.UpdateFrame. Falloff is
        // whatever the normal BFS decay produces from here, same as a sky opening - no separate
        // radius concept needed.
        public IEnumerable<Vector2> GetLightSourcePositions()
        {
            for (int i = 0; i < torches.Count; i++)
                yield return torches[i].LightOrigin;
        }

        public void Restore(IEnumerable<TorchSaveData> savedTorches)
        {
            torches.Clear();
            if (savedTorches != null)
            {
                foreach (TorchSaveData savedTorch in savedTorches)
                {
                    if (savedTorch != null)
                        torches.Add(new TorchInstance(new Vector2(savedTorch.PositionX, savedTorch.PositionY)));
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public void MarkPersisted()
        {
            revisions.MarkPersisted();
        }

        public bool TryPlaceSelectedTorch(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);

            if (!input.PlacePressed)
                return false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.Torch)
                return false;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return true;

            if (!previewValid)
                return true;

            torches.Add(new TorchInstance(new Vector2(previewBounds.X, previewBounds.Y)));
            revisions.MarkChanged();
            selectedSlot.RemoveOne();
            return true;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < torches.Count; i++)
                spriteBatch.Draw(Texture, torches[i].Bounds, PlaceholderSource, PlaceholderTint);

            if (previewVisible)
                spriteBatch.Draw(Texture, previewBounds, PlaceholderSource, previewValid ? ValidPreviewTint : InvalidPreviewTint);
        }

        public bool TryGetMiningTargetAtTile(Point tile, out WorldObjectMiningTarget target)
        {
            if (TryGetTorchIndexAtTile(tile, out int index))
            {
                target = new WorldObjectMiningTarget(MiningDefinition, torches[index].Bounds);
                return true;
            }

            target = default;
            return false;
        }

        public bool TryMineObjectAtTile(Point tile, WorldItemRuntimeSystem worldItemRuntimeSystem)
        {
            if (worldItemRuntimeSystem == null || !TryGetTorchIndexAtTile(tile, out int index))
                return false;

            TorchInstance torch = torches[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.Torch, 1, torch.Bounds.Center.ToVector2());
            torches.RemoveAt(index);
            revisions.MarkChanged();
            return true;
        }

        public void OnForegroundTileBroken(ForegroundTileBrokenContext context)
        {
            if (WorldObjectSupport.RemoveObjectsWithBrokenBaseSupport(
                torches,
                context.Tile,
                WorldMap,
                torch => context.WorldItemRuntimeSystem.SpawnItemDrops(ItemId.Torch, 1, torch.Bounds.Center.ToVector2())))
            {
                revisions.MarkChanged();
            }
        }

        private Rectangle GetSnappedPlacementBounds(Point tile)
        {
            int x = (WorldMap.WrapTileX(tile.X) * WorldMap.TileSize) + ((WorldMap.TileSize - TorchWidth) / 2);
            int y = ((tile.Y + 1) * WorldMap.TileSize) - TorchHeight;
            return new Rectangle(x, y, TorchWidth, TorchHeight);
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            previewVisible = false;
            previewValid = false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.Torch)
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
                   !IntersectsExistingTorch(bounds);
        }

        private bool IntersectsExistingTorch(Rectangle bounds)
        {
            for (int i = 0; i < torches.Count; i++)
            {
                if (torches[i].Bounds.Intersects(bounds))
                    return true;
            }

            return false;
        }

        private bool TryGetTorchIndexAtTile(Point tile, out int index)
        {
            index = -1;
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return false;

            Rectangle tileBounds = WorldMap.GetTileBounds(WorldMap.WrapTileX(tile.X), tile.Y);
            for (int i = 0; i < torches.Count; i++)
            {
                if (!torches[i].Bounds.Intersects(tileBounds))
                    continue;

                index = i;
                return true;
            }

            return false;
        }
    }
}
