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
    public sealed class WorkbenchRuntimeSystem : FurnitureRuntimeSystem<WorkbenchInstance>
    {
        public const int WorkbenchWidth = 24;
        public const int WorkbenchHeight = 16;
        private const int HoverPadding = 8;

        private static readonly WorldObjectMiningDefinition MiningDefinition = new(true, 1.5f, 1);
        private static readonly Rectangle NormalSource = new Rectangle(0, 0, WorkbenchWidth, WorkbenchHeight);
        private static readonly Rectangle SelectedSource = new Rectangle(WorkbenchWidth, 0, WorkbenchWidth, WorkbenchHeight);

        private int hoveredWorkbenchIndex = -1;
        public required Texture2D Texture { get; init; }

        public IReadOnlyList<WorkbenchInstance> Workbenches => FurnitureItems;

        public void Restore(IEnumerable<WorkbenchSaveData> savedWorkbenches)
        {
            furnitureItems.Clear();
            if (savedWorkbenches != null)
            {
                foreach (WorkbenchSaveData savedWorkbench in savedWorkbenches)
                {
                    if (savedWorkbench != null)
                    {
                        Point tile = new Point(WorldMap.WrapTileX((int)(savedWorkbench.PositionX / WorldMap.TileSize)),
                                               (int)(savedWorkbench.PositionY / WorldMap.TileSize) + 1);
                        bool facingLeft = savedWorkbench.FacingLeft;
                        furnitureItems.Add(new WorkbenchInstance(tile, WorldMap.TileSize, facingLeft));
                    }
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public void UpdateHover(Vector2 mouseWorld)
        {
            hoveredWorkbenchIndex = -1;

            for (int i = 0; i < furnitureItems.Count; i++)
            {
                Rectangle bounds = furnitureItems[i].Bounds;
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
            return InteractionFinder.TryGetNearest(furnitureItems, Player, out _)
                ? CraftTier.Workbench
                : CraftTier.Basic;
        }

        public bool TryInteract(Player player, out InteractionResult result)
        {
            if (InteractionFinder.TryGetNearest(furnitureItems, player, out WorkbenchInstance workbench))
            {
                result = workbench.Interact(player);
                return result != InteractionResult.None;
            }

            result = InteractionResult.None;
            return false;
        }

        public bool TryPlaceSelectedWorkbench(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);
            return TryPlaceFurniture(input, selectedHotbarIndex, mouseWorld, ItemId.Workbench, tile =>
            {
                furnitureItems.Add(new WorkbenchInstance(tile, WorldMap.TileSize, previewFacingLeft));
            });
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            base.UpdatePlacementPreview(selectedHotbarIndex, mouseWorld, ItemId.Workbench, WorkbenchWidth, WorkbenchHeight);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                WorkbenchInstance workbench = furnitureItems[i];
                Rectangle bounds = workbench.Bounds;
                Rectangle source = i == hoveredWorkbenchIndex ? SelectedSource : NormalSource;
                SpriteEffects effects = workbench.FacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(Texture, bounds, source, Color.White, 0f, Vector2.Zero, effects, 0f);
            }

            if (previewVisible)
            {
                WorkbenchInstance preview = new WorkbenchInstance(previewBaseTile, WorldMap.TileSize, previewFacingLeft);
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

            WorkbenchInstance workbench = furnitureItems[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.Workbench, 1, workbench.InteractionPosition);
            furnitureItems.RemoveAt(index);
            revisions.MarkChanged();
            return true;
        }

        public override void OnForegroundTileBroken(ForegroundTileBrokenContext context)
        {
            if (WorldObjectSupport.RemoveObjectsWithBrokenBaseSupport(furnitureItems, context.Tile, WorldMap, workbench =>
                context.WorldItemRuntimeSystem.SpawnItemDrops(ItemId.Workbench, 1, workbench.InteractionPosition)))
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
