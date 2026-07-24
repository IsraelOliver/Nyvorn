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
    public sealed class DoorRuntimeSystem : FurnitureRuntimeSystem<DoorInstance>, IWorldObjectMovementBlocker
    {
        public const int ClosedWidth = 8;
        public const int OpenWidth = 16;
        public const int DoorHeight = 24;

        private static readonly WorldObjectMiningDefinition MiningDefinition = new(true, 1f, 1);
        private static readonly Rectangle ClosedSource = new Rectangle(0, 0, ClosedWidth, DoorHeight);
        private static readonly Rectangle OpenSource = new Rectangle(ClosedWidth, 0, OpenWidth, DoorHeight);

        public required Texture2D Texture { get; init; }

        public IReadOnlyList<DoorInstance> Doors => FurnitureItems;
        public int Revision => revisions.Revision;

        public void Restore(IEnumerable<DoorSaveData> savedDoors)
        {
            furnitureItems.Clear();
            if (savedDoors != null)
            {
                foreach (DoorSaveData savedDoor in savedDoors)
                {
                    if (savedDoor == null)
                        continue;

                    Point tile = new Point(WorldMap.WrapTileX(savedDoor.TileX), savedDoor.TileY);
                    if (!WorldMap.InBounds(tile.X, tile.Y))
                        continue;

                    bool facingLeft = savedDoor.FacingLeft;
                    furnitureItems.Add(new DoorInstance(tile, WorldMap.TileSize, savedDoor.IsOpen, savedDoor.OpensRight, facingLeft));
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public bool TryInteract(Player player)
        {
            if (!InteractionFinder.TryGetNearest(furnitureItems, player, out DoorInstance door))
                return false;

            bool toggled = door.TryToggle(player);
            if (toggled)
                revisions.MarkChanged();

            return true;
        }

        public bool TryPlaceSelectedDoor(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);

            return TryPlaceFurniture(input, selectedHotbarIndex, mouseWorld, ItemId.WoodDoor, baseTile =>
            {
                furnitureItems.Add(new DoorInstance(baseTile, WorldMap.TileSize, false, true, previewFacingLeft));
            });
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                DoorInstance door = furnitureItems[i];
                Rectangle source = door.IsOpen ? OpenSource : ClosedSource;
                Rectangle destination = door.DrawBounds;
                SpriteEffects effects = door.IsOpen && !door.OpensRight
                    ? SpriteEffects.FlipHorizontally
                    : SpriteEffects.None;
                if (door.FacingLeft)
                    effects = effects == SpriteEffects.FlipHorizontally ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

                spriteBatch.Draw(Texture, destination, source, Color.White, 0f, Vector2.Zero, effects, 0f);
            }

            if (previewVisible)
            {
                DoorInstance preview = new DoorInstance(previewBaseTile, WorldMap.TileSize, false, true, previewFacingLeft);
                SpriteEffects effects = previewFacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(Texture, preview.Bounds, ClosedSource, previewValid ? ValidPreviewTint : InvalidPreviewTint, 0f, Vector2.Zero, effects, 0f);
            }
        }

        public override bool IsObjectOccupyingTile(int tileX, int tileY)
        {
            return TryGetFurnitureIndexAtTile(new Point(tileX, tileY), _ => true, out _);
        }

        public override bool TryGetMiningTargetAtTile(Point tile, out WorldObjectMiningTarget target)
        {
            if (TryGetFurnitureIndexAtTile(tile, _ => true, out int index))
            {
                target = new WorldObjectMiningTarget(MiningDefinition, furnitureItems[index].Bounds);
                return true;
            }

            target = default;
            return false;
        }

        public override bool TryMineObjectAtTile(Point tile, WorldItemRuntimeSystem worldItemRuntimeSystem)
        {
            if (worldItemRuntimeSystem == null || !TryGetFurnitureIndexAtTile(tile, _ => true, out int index))
                return false;

            DoorInstance door = furnitureItems[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.WoodDoor, 1, door.InteractionPosition);
            furnitureItems.RemoveAt(index);
            revisions.MarkChanged();
            return true;
        }

        public bool IsMovementBlockingTile(int tileX, int tileY)
        {
            if (!WorldMap.InBounds(tileX, tileY))
                return false;

            Rectangle tileBounds = WorldMap.GetTileBounds(WorldMap.WrapTileX(tileX), tileY);
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                DoorInstance door = furnitureItems[i];
                if (!door.IsOpen && door.Bounds.Intersects(tileBounds))
                    return true;
            }

            return false;
        }

        public override void OnForegroundTileBroken(ForegroundTileBrokenContext context)
        {
            if (!WorldMap.InBounds(context.Tile.X, context.Tile.Y))
                return;

            Point wrappedTile = new Point(WorldMap.WrapTileX(context.Tile.X), context.Tile.Y);
            bool removedAny = false;

            for (int i = furnitureItems.Count - 1; i >= 0; i--)
            {
                DoorInstance door = furnitureItems[i];
                if (!door.IsAffectedByBrokenForegroundTile(wrappedTile))
                    continue;

                context.WorldItemRuntimeSystem.SpawnItemDrops(ItemId.WoodDoor, 1, door.InteractionPosition);
                furnitureItems.RemoveAt(i);
                removedAny = true;
            }

            if (removedAny)
                revisions.MarkChanged();
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

            previewBaseTile = new Point(WorldMap.WrapTileX(baseTile.X), baseTile.Y);
            previewVisible = true;

            DoorInstance preview = new DoorInstance(previewBaseTile, WorldMap.TileSize);
            previewValid = ValidatePlacement(preview.Bounds);
        }

        protected override bool ValidateSupport(Rectangle bounds)
        {
            int topSupportTileY = (bounds.Top / WorldMap.TileSize) - 1;
            int tileX = bounds.Left / WorldMap.TileSize;
            return WorldObjectSupport.HasFullBaseSupport(WorldMap, bounds) &&
                   WorldMap.IsSolidAt(tileX, topSupportTileY);
        }

        protected override bool IntersectsExisting(Rectangle bounds)
        {
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                if (furnitureItems[i].Bounds.Intersects(bounds))
                    return true;
            }

            return false;
        }
    }
}
