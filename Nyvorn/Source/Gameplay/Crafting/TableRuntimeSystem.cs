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
    public sealed class TableRuntimeSystem : FurnitureRuntimeSystem<TableInstance>, IPlatform
    {
        private const int TableWidth = 24;
        private const int TableHeight = 16;

        private static readonly WorldObjectMiningDefinition MiningDefinition = new(true, 1.5f, 1);
        private static readonly Rectangle NormalSource = new Rectangle(0, 0, TableWidth, TableHeight);

        public required Texture2D Texture { get; init; }

        public IReadOnlyList<TableInstance> Tables => FurnitureItems;

        public void Restore(IEnumerable<TableSaveData> savedTables)
        {
            furnitureItems.Clear();
            if (savedTables != null)
            {
                foreach (TableSaveData savedTable in savedTables)
                {
                    if (savedTable != null)
                    {
                        int offset = (WorldMap.TileSize - 24) / 2;
                        Point tile = new Point(WorldMap.WrapTileX((int)System.Math.Round((savedTable.PositionX - offset) / WorldMap.TileSize)),
                                               (int)System.Math.Round(((savedTable.PositionY + 16) / WorldMap.TileSize) - 1));
                        bool facingLeft = savedTable.FacingLeft;
                        furnitureItems.Add(new TableInstance(tile, WorldMap.TileSize, facingLeft));
                    }
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }


        public bool TryPlaceSelectedTable(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);
            return TryPlaceFurniture(input, selectedHotbarIndex, mouseWorld, ItemId.Table, tile =>
            {
                furnitureItems.Add(new TableInstance(tile, WorldMap.TileSize, previewFacingLeft));
            });
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            base.UpdatePlacementPreview(selectedHotbarIndex, mouseWorld, ItemId.Table, TableWidth, TableHeight);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (Texture == null)
                return;

            for (int i = 0; i < furnitureItems.Count; i++)
            {
                TableInstance table = furnitureItems[i];
                Rectangle bounds = table.Bounds;
                SpriteEffects effects = table.FacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(Texture, bounds, NormalSource, Color.White, 0f, Vector2.Zero, effects, 0f);
            }

            if (previewVisible)
            {
                TableInstance preview = new TableInstance(previewBaseTile, WorldMap.TileSize, previewFacingLeft);
                SpriteEffects effects = previewFacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(Texture, preview.Bounds, NormalSource, previewValid ? ValidPreviewTint : InvalidPreviewTint, 0f, Vector2.Zero, effects, 0f);
            }
        }

        public override bool IsObjectOccupyingTile(int tileX, int tileY)
        {
            return TryGetFurnitureIndexAtTile(new Point(tileX, tileY), _ => true, out _);
        }

        public bool IsPlatformBlockingMovement(Rectangle footSensor, Vector2 playerVelocity)
        {
            // Only block downward movement (falling)
            if (playerVelocity.Y <= 0f)
                return false;

            // Check if player's feet touch any table
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                if (footSensor.Intersects(furnitureItems[i].Bounds))
                    return true;
            }

            return false;
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

            TableInstance table = furnitureItems[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.Table, 1, table.InteractionPosition);
            furnitureItems.RemoveAt(index);
            revisions.MarkChanged();
            return true;
        }

        public override void OnForegroundTileBroken(ForegroundTileBrokenContext context)
        {
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
