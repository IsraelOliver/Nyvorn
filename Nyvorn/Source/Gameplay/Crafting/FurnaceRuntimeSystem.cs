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
    public sealed class FurnaceRuntimeSystem : FurnitureRuntimeSystem<FurnaceInstance>
    {
        public const int FurnaceWidth = 24;
        public const int FurnaceHeight = 16;
        private const int HoverPadding = 8;

        private static readonly WorldObjectMiningDefinition MiningDefinition = new(true, 1.5f, 1);
        private static readonly Rectangle NormalSource = new Rectangle(0, 0, FurnaceWidth, FurnaceHeight);
        private static readonly Rectangle SelectedSource = new Rectangle(FurnaceWidth, 0, FurnaceWidth, FurnaceHeight);

        private int hoveredFurnaceIndex = -1;
        public required Texture2D Texture { get; init; }

        public IReadOnlyList<FurnaceInstance> Furnaces => FurnitureItems;

        public void Restore(IEnumerable<FurnaceSaveData> savedFurnaces)
        {
            furnitureItems.Clear();
            if (savedFurnaces != null)
            {
                foreach (FurnaceSaveData savedFurnace in savedFurnaces)
                {
                    if (savedFurnace != null)
                    {
                        int offset = (WorldMap.TileSize - FurnaceWidth) / 2;
                        Point tile = new Point(WorldMap.WrapTileX((int)System.Math.Round((savedFurnace.PositionX - offset) / WorldMap.TileSize)),
                                               (int)System.Math.Round(((savedFurnace.PositionY + FurnaceHeight) / WorldMap.TileSize) - 1));
                        bool facingLeft = savedFurnace.FacingLeft;
                        furnitureItems.Add(new FurnaceInstance(tile, WorldMap.TileSize, facingLeft));
                    }
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public void UpdateHover(Vector2 mouseWorld)
        {
            hoveredFurnaceIndex = -1;

            for (int i = 0; i < furnitureItems.Count; i++)
            {
                Rectangle bounds = furnitureItems[i].Bounds;
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
            return InteractionFinder.TryGetNearest(furnitureItems, Player, out _)
                ? CraftTier.Furnace
                : CraftTier.Basic;
        }

        public bool TryInteract(Player player, out InteractionResult result)
        {
            if (InteractionFinder.TryGetNearest(furnitureItems, player, out FurnaceInstance furnace))
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
            return TryPlaceFurniture(input, selectedHotbarIndex, mouseWorld, ItemId.Furnace, tile =>
            {
                furnitureItems.Add(new FurnaceInstance(tile, WorldMap.TileSize, previewFacingLeft));
            });
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            base.UpdatePlacementPreview(selectedHotbarIndex, mouseWorld, ItemId.Furnace, FurnaceWidth, FurnaceHeight);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                FurnaceInstance furnace = furnitureItems[i];
                Rectangle bounds = furnace.Bounds;
                Rectangle source = i == hoveredFurnaceIndex ? SelectedSource : NormalSource;
                SpriteEffects effects = furnace.FacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(Texture, bounds, source, Color.White, 0f, Vector2.Zero, effects, 0f);
            }

            if (previewVisible)
            {
                FurnaceInstance preview = new FurnaceInstance(previewBaseTile, WorldMap.TileSize, previewFacingLeft);
                SpriteEffects effects = previewFacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(Texture, preview.Bounds, NormalSource, previewValid ? ValidPreviewTint : InvalidPreviewTint, 0f, Vector2.Zero, effects, 0f);
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

            FurnaceInstance furnace = furnitureItems[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.Furnace, 1, furnace.InteractionPosition);
            furnitureItems.RemoveAt(index);
            revisions.MarkChanged();
            return true;
        }

        public override void OnForegroundTileBroken(ForegroundTileBrokenContext context)
        {
            if (WorldObjectSupport.RemoveObjectsWithBrokenBaseSupport(furnitureItems, context.Tile, WorldMap, furnace =>
                context.WorldItemRuntimeSystem.SpawnItemDrops(ItemId.Furnace, 1, furnace.InteractionPosition)))
                revisions.MarkChanged();
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
