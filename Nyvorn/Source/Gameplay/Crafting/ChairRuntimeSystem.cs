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
    public sealed class ChairRuntimeSystem : FurnitureRuntimeSystem<ChairInstance>
    {
        private const int ChairWidth = 8;
        private const int ChairHeight = 16;
        private const int HoverPadding = 8;

        private static readonly WorldObjectMiningDefinition MiningDefinition = new(true, 1.5f, 1);
        private static readonly Rectangle NormalSource = new Rectangle(0, 0, ChairWidth, ChairHeight);
        private static readonly Rectangle SelectedSource = new Rectangle(ChairWidth, 0, ChairWidth, ChairHeight);

        private int hoveredChairIndex = -1;
        public required Texture2D Texture { get; init; }

        public IReadOnlyList<ChairInstance> Chairs => FurnitureItems;

        public void Restore(IEnumerable<ChairSaveData> savedChairs)
        {
            furnitureItems.Clear();
            if (savedChairs != null)
            {
                foreach (ChairSaveData savedChair in savedChairs)
                {
                    if (savedChair != null)
                    {
                        int offset = (WorldMap.TileSize - 8) / 2;
                        Point tile = new Point(WorldMap.WrapTileX((int)System.Math.Round((savedChair.PositionX - offset) / WorldMap.TileSize)),
                                               (int)System.Math.Round(((savedChair.PositionY + 16) / WorldMap.TileSize) - 1));
                        bool facingLeft = savedChair.FacingLeft;
                        furnitureItems.Add(new ChairInstance(tile, WorldMap.TileSize, facingLeft));
                    }
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public void UpdateHover(Vector2 mouseWorld)
        {
            hoveredChairIndex = -1;

            for (int i = 0; i < furnitureItems.Count; i++)
            {
                Rectangle bounds = furnitureItems[i].Bounds;
                Rectangle hoverBounds = bounds;
                hoverBounds.Inflate(HoverPadding, HoverPadding);
                if (!hoverBounds.Contains(mouseWorld))
                    continue;

                if (Vector2.Distance(Player.Position, bounds.Center.ToVector2()) > Player.WorldInteractionRange)
                    continue;

                hoveredChairIndex = i;
                return;
            }
        }

        public bool TryPlaceSelectedChair(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);
            return TryPlaceFurniture(input, selectedHotbarIndex, mouseWorld, ItemId.Chair, tile =>
            {
                furnitureItems.Add(new ChairInstance(tile, WorldMap.TileSize, previewFacingLeft));
            });
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            base.UpdatePlacementPreview(selectedHotbarIndex, mouseWorld, ItemId.Chair, ChairWidth, ChairHeight);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (Texture == null)
                return;

            for (int i = 0; i < furnitureItems.Count; i++)
            {
                ChairInstance chair = furnitureItems[i];
                Rectangle bounds = chair.Bounds;
                Rectangle source = i == hoveredChairIndex ? SelectedSource : NormalSource;
                SpriteEffects effects = chair.FacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(Texture, bounds, source, Color.White, 0f, Vector2.Zero, effects, 0f);
            }

            if (previewVisible)
            {
                ChairInstance preview = new ChairInstance(previewBaseTile, WorldMap.TileSize, previewFacingLeft);
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

            ChairInstance chair = furnitureItems[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.Chair, 1, chair.InteractionPosition);
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
