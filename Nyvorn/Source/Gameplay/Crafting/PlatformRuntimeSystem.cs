using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Interaction;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Persistence;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class PlatformRuntimeSystem : FurnitureRuntimeSystem<PlatformInstance>, IRaycastCollider, IPlatform
    {
        private const int PlatformWidth = 8;
        private const int PlatformHeight = 8;
        private const int SpriteSheetSpacing = 1;

        private static readonly WorldObjectMiningDefinition MiningDefinition = new(true, 1f, 1);

        public required Texture2D Texture { get; init; }

        public IReadOnlyList<PlatformInstance> Platforms => FurnitureItems;

        public void Restore(IEnumerable<PlatformSaveData> savedPlatforms)
        {
            furnitureItems.Clear();
            if (savedPlatforms != null)
            {
                foreach (PlatformSaveData savedPlatform in savedPlatforms)
                {
                    if (savedPlatform != null)
                    {
                        Point tile = new Point(
                            WorldMap.WrapTileX((int)System.Math.Round(savedPlatform.PositionX / WorldMap.TileSize)),
                            (int)System.Math.Round((savedPlatform.PositionY / WorldMap.TileSize)));
                        furnitureItems.Add(new PlatformInstance(tile, WorldMap.TileSize));
                    }
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public bool TryPlaceSelectedPlatform(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);
            return TryPlaceFurniture(input, selectedHotbarIndex, mouseWorld, ItemId.Platform, tile =>
            {
                furnitureItems.Add(new PlatformInstance(tile, WorldMap.TileSize));
            });
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            base.UpdatePlacementPreview(selectedHotbarIndex, mouseWorld, ItemId.Platform, PlatformWidth, PlatformHeight);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (Texture == null)
                return;

            for (int i = 0; i < furnitureItems.Count; i++)
            {
                PlatformInstance platform = furnitureItems[i];
                Rectangle sourceRect = GetAutoTiledSource(platform.Tile);
                spriteBatch.Draw(Texture, platform.Bounds, sourceRect, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            }

            if (previewVisible)
            {
                PlatformInstance preview = new PlatformInstance(previewBaseTile, WorldMap.TileSize);
                Rectangle sourceRect = GetAutoTiledSource(preview.Tile);
                spriteBatch.Draw(Texture, preview.Bounds, sourceRect, previewValid ? ValidPreviewTint : InvalidPreviewTint, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            }
        }

        private Rectangle GetAutoTiledSource(Point platformTile)
        {
            bool hasLeftPlatform = HasAdjacentPlatform(platformTile.X - 1, platformTile.Y);
            bool hasRightPlatform = HasAdjacentPlatform(platformTile.X + 1, platformTile.Y);
            bool hasLeftSolid = HasAdjacentSolid(platformTile.X - 1, platformTile.Y);
            bool hasRightSolid = HasAdjacentSolid(platformTile.X + 1, platformTile.Y);

            int lineY = 0;
            int colX = 0;

            if (hasLeftSolid || hasRightSolid)
            {
                lineY = 1;
                if (hasLeftSolid && !hasRightPlatform && !hasRightSolid)
                    colX = 0;
                else if (!hasLeftSolid && !hasLeftPlatform && hasRightSolid)
                    colX = 1;
                else if (hasLeftSolid && hasRightPlatform)
                    colX = 2;
                else if (hasLeftSolid && hasRightSolid)
                    colX = 3;
                else if (hasLeftPlatform && hasRightSolid)
                    colX = 4;
                else if (!hasLeftSolid && !hasLeftPlatform)
                    colX = 0;
                else
                    colX = 1;
            }
            else
            {
                lineY = 0;
                if (!hasLeftPlatform && !hasRightPlatform)
                    colX = 0;
                else if (!hasLeftPlatform && hasRightPlatform)
                    colX = 2;
                else if (hasLeftPlatform && !hasRightPlatform)
                    colX = 4;
                else
                    colX = 3;
            }

            int sourceX = colX * (PlatformWidth + SpriteSheetSpacing);
            int sourceY = lineY * (PlatformHeight + SpriteSheetSpacing);

            return new Rectangle(sourceX, sourceY, PlatformWidth, PlatformHeight);
        }

        private bool HasAdjacentPlatform(int tileX, int tileY)
        {
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                if (furnitureItems[i].Tile.X == tileX && furnitureItems[i].Tile.Y == tileY)
                    return true;
            }
            return false;
        }

        private bool HasAdjacentSolid(int tileX, int tileY)
        {
            TileType tileType = WorldMap.GetTile(tileX, tileY);
            return tileType != TileType.Empty;
        }

        public override bool IsObjectOccupyingTile(int tileX, int tileY)
        {
            return TryGetFurnitureIndexAtTile(new Point(tileX, tileY), _ => true, out _);
        }

        public bool TryGetCollision(Vector2 previousPosition, Vector2 currentPosition, out CollisionRaycast collision)
        {
            collision = default;

            for (int i = 0; i < furnitureItems.Count; i++)
            {
                CollisionRaycast platformCollision = CollisionRaycast.TestMovement(
                    previousPosition,
                    currentPosition,
                    furnitureItems[i].Bounds);

                if (platformCollision.Intersects)
                {
                    collision = platformCollision;
                    return true;
                }
            }

            return false;
        }

        public Rectangle GetPlatformSurface(int tableIndex)
        {
            if (tableIndex < 0 || tableIndex >= furnitureItems.Count)
                return Rectangle.Empty;

            return furnitureItems[tableIndex].SurfaceBounds;
        }

        public int GetPlatformCount()
        {
            return furnitureItems.Count;
        }

        public Rectangle GetPlatformBounds(int tableIndex)
        {
            if (tableIndex < 0 || tableIndex >= furnitureItems.Count)
                return Rectangle.Empty;

            return furnitureItems[tableIndex].Bounds;
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

            PlatformInstance platform = furnitureItems[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.Platform, 1, platform.InteractionPosition);
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

        public bool IsPlatformBlockingMovement(Rectangle footSensor, Vector2 playerVelocity)
        {
            if (playerVelocity.Y <= 0f)
                return false;

            for (int i = 0; i < furnitureItems.Count; i++)
            {
                if (furnitureItems[i].SurfaceBounds.Intersects(footSensor))
                    return true;
            }

            return false;
        }

        protected override bool ValidateSupport(Rectangle bounds)
        {
            // Get tile coordinates from bounds
            int tileCenterX = bounds.Center.X / WorldMap.TileSize;
            int tileCenterY = bounds.Center.Y / WorldMap.TileSize;

            // Check if has adjacent tile or platform
            foreach (int offsetX in new[] { -1, 0, 1 })
            {
                foreach (int offsetY in new[] { -1, 0, 1 })
                {
                    if (offsetX == 0 && offsetY == 0)
                        continue;

                    int checkX = WorldMap.WrapTileX(tileCenterX + offsetX);
                    int checkY = tileCenterY + offsetY;

                    if (WorldMap.InBounds(checkX, checkY))
                    {
                        // Check foreground tile
                        if (WorldMap.GetTile(checkX, checkY) != TileType.Empty)
                            return true;

                        // Check background tile
                        if (WorldMap.GetBackgroundTile(checkX, checkY) != TileType.Empty)
                            return true;
                    }
                }
            }

            // Check if has adjacent platform
            Rectangle platformCheckBounds = new Rectangle(
                bounds.X - WorldMap.TileSize,
                bounds.Y - WorldMap.TileSize,
                bounds.Width + (WorldMap.TileSize * 2),
                bounds.Height + (WorldMap.TileSize * 2)
            );

            for (int i = 0; i < furnitureItems.Count; i++)
            {
                if (platformCheckBounds.Intersects(furnitureItems[i].Bounds))
                    return true;
            }

            return false;
        }
    }
}
